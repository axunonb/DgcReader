﻿using System;
using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using PeterO.Cbor;
using System.Threading.Tasks;
using GreenpassReader.Models;
using DgcReader.Cwt.Cose;
using DgcReader.Exceptions;
using DgcReader.Models;
using Microsoft.Extensions.Logging;
using DgcReader.Interfaces.RulesValidators;
using DgcReader.Interfaces.TrustListProviders;
using DgcReader.Interfaces.BlacklistProviders;

// Copyright (c) 2021 Davide Trevisan
// Licensed under the Apache License, Version 2.0

namespace DgcReader
{
    /// <summary>
    /// Service for decoding and verifying the signature of the European Digital Green Certificate (Green pass)
    /// </summary>
    public class DgcReaderService
    {
        private readonly ITrustListProvider TrustListProvider;
        private readonly IBlacklistProvider? BlackListProvider;
        private readonly IRulesValidator? RulesValidator;
        private readonly ILogger? Logger;

        /// <summary>
        /// Instantiate the DGCDecoderService
        /// </summary>
        /// <param name="trustListProvider">The provider used to retrieve the valid public keys for signature validations</param>
        /// <param name="blackListProvider">The provider used to check if a certificate is blacklisted</param>
        /// <param name="rulesValidator">The service used to validate the rules for a specific country</param>
        /// <param name="logger"></param>
        public DgcReaderService(ITrustListProvider trustListProvider = null,
            IBlacklistProvider? blackListProvider = null,
            IRulesValidator? rulesValidator = null,
            ILogger<DgcReaderService>? logger = null)
        {
            TrustListProvider = trustListProvider;
            BlackListProvider = blackListProvider;
            RulesValidator = rulesValidator;
            Logger = logger;
        }


        /// <summary>
        /// Decodes the DGC data, trowing exceptions only if data is in invalid format
        /// Informations about signature validity and expiration can be found in the returned result
        /// </summary>
        /// <param name="qrCodeData">DGC raw data from the QRCode</param>
        /// <returns></returns>
        public Task<SignedDgc> Decode(string qrCodeData)
        {
            return Decode(qrCodeData, DateTimeOffset.Now);
        }

        /// <summary>
        /// Decodes the DGC data, trowing exceptions only if data is in invalid format
        /// Informations about signature validity and expiration can be found in the returned result
        /// </summary>
        /// <param name="qrCodeData">DGC raw data from the QRCode</param>
        /// <param name="validationInstant"></param>
        /// <returns></returns>
        public async Task<SignedDgc> Decode(string qrCodeData, DateTimeOffset validationInstant)
        {
            try
            {
                var result = new SignedDgc() { ValidationInstant = validationInstant };

                // Step 1: decode
                var cose = DecodeCoseObject(qrCodeData);
                var dgc = GetSignedDgc(cose);

                // Step 2: check signature
                try
                {
                    await VerifySignature(cose, validationInstant);
                    result.HasValidSignature = true;
                }
                catch (Exception e)
                {
                    Logger?.LogWarning($"Verify signature failed: {e.Message}");
                    result.HasValidSignature = false;
                    return result;
                }



                return result;
            }
            catch (Exception e)
            {
                Logger?.LogError($"Error decoding Dgc data: {e.Message}");
                throw;
            }
        }


        /// <summary>
        /// Decodes the DGC data, verifying signature, blacklist and rules if a provider is available.
        /// </summary>
        /// <param name="qrCodeData">The QRCode data of the DGC</param>
        /// <param name="throwOnError">If true, throw an exception if the validation fails</param>
        /// <returns></returns>
        /// 
        public Task<DgcValidationResult> Verify(string qrCodeData, bool throwOnError = true)
        {
            return Verify(qrCodeData, DateTimeOffset.Now, throwOnError);
        }


        /// <summary>
        /// Decodes the DGC data, verifying signature, blacklist and rules if a provider is available.
        /// This overload is intended for testing purposes only
        /// </summary>
        /// <param name="qrCodeData">The QRCode data of the DGC</param>
        /// <param name="validationInstant">The instant for the validation of the object (for testing purposes)</param>
        /// <param name="throwOnError">If true, throw an exception if the validation fails</param>
        /// <returns></returns>
        /// <exception cref="DgcException"></exception>
        public async Task<DgcValidationResult> Verify(string qrCodeData, DateTimeOffset validationInstant, bool throwOnError = true)
        {
            var result = new DgcValidationResult()
            {
                ValidationInstant = validationInstant,
                Status = DgcResultStatus.NotEuDCC,
            };

            try
            {
                // Inner try/catch block to calculate exception and result
                try
                {
                    var cose = DecodeCoseObject(qrCodeData);

                    // Step 1: Decoding data
                    var signedDgc = GetSignedDgc(cose);

                    result.Issuer = signedDgc.Issuer;
                    result.IssuedDate = signedDgc.IssuedDate;
                    result.SignatureExpiration = signedDgc.ExpirationDate;
                    result.Dgc = signedDgc.Dgc;

                    // Step 2: check signature
                    if (TrustListProvider == null)
                    {
                        throw new DgcSignatureValidationException($"No trustlist provider is registered for signature validation");
                    }
                    // Checking signature first, throwing exceptions if not valid
                    await VerifySignature(cose, validationInstant);

                    // Signature already checked
                    result.HasValidSignature = true;
                    result.Status = DgcResultStatus.NeedRulesVerification;

                    // Step 3: check blacklist
                    if (BlackListProvider != null)
                    {
                        var certEntry = result.Dgc.GetCertificateEntry();
                        var blacklisted = await BlackListProvider.IsBlacklisted(certEntry.CertificateIdentifier);

                        // Check performed
                        result.BlaclistVerified = true;
                        if (blacklisted)
                        {
                            throw new DgcBlackListException($"The certificate is blacklisted", certEntry.CertificateIdentifier);
                        }
                    }
                    else
                    {
                        Logger?.LogWarning($"No blacklist provider is registered, blacklist validation is skipped");
                    }

                    // Step 4: check country rules
                    if (RulesValidator != null)
                    {
                        var rulesResult = await RulesValidator.GetRulesValidationResult(result.Dgc, result.ValidationInstant);
                        result.ValidFrom = rulesResult.ValidFrom;
                        result.ValidUntil = rulesResult.ValidUntil;
                        result.RulesVerificationCountry = rulesResult.RulesVerificationCountry;
                        result.Status = rulesResult.Status;

                        if (throwOnError)
                        {
                            if (rulesResult.Status != DgcResultStatus.Valid &&
                                rulesResult.Status != DgcResultStatus.PartiallyValid)
                            {
                                throw new DgcRulesValidationException(GetDgcResultStatusDescription(rulesResult.Status), rulesResult);
                            }
                        }
                    }
                    else
                    {
                        result.Status = DgcResultStatus.NeedRulesVerification;
                        Logger?.LogWarning($"No rules validator is registered, rules validation is skipped");
                    }

                }
                catch (DgcSignatureValidationException)
                {
                    result.Status = DgcResultStatus.InvalidSignature;
                    throw;
                }
                catch (DgcBlackListException)
                {
                    result.Status = DgcResultStatus.Blacklisted;
                    throw;
                }
                catch (DgcRulesValidationException e)
                {
                    result.ValidFrom = e.ValidFrom;
                    result.ValidUntil = e.ValidUntil;
                    result.RulesVerificationCountry = e.RulesVerificationCountry;
                    result.Status = e.Status;
                    throw;
                }
                catch (DgcException)
                {
                    result.Status = DgcResultStatus.NotValid;
                    throw;
                }
                catch (Exception e)
                {
                    result.Status = DgcResultStatus.NotValid;
                    throw new DgcException(e.Message, e);
                }
            }
            catch (DgcException e)
            {
                if (throwOnError)
                {
                    Logger?.LogError($"Validation failed: {e.Message}");
                    throw;
                }
            }

            if (result.Status == DgcResultStatus.Valid)
                Logger?.LogInformation($"Validation succeded: {GetDgcResultStatusDescription(result.Status)}");
            else if (result.Status == DgcResultStatus.PartiallyValid)
                Logger?.LogWarning($"Validation succeded: {GetDgcResultStatusDescription(result.Status)}");
            else if (result.Status == DgcResultStatus.NeedRulesVerification && RulesValidator == null)
                Logger?.LogWarning($"Validation succeded: {GetDgcResultStatusDescription(result.Status)}");
            else
                Logger?.LogError($"Validation failed: {GetDgcResultStatusDescription(result.Status)}");

            return result;
        }


        /// <summary>
        /// Decodes the DGC data, verifying signature, blacklist and rules if a provider is available.
        /// A result is always returned
        /// </summary>
        /// <param name="qrCodeData">The QRCode data of the DGC</param>
        /// <returns></returns>
        public Task<DgcValidationResult> GetValidationResult(string qrCodeData)
        {
            return Verify(qrCodeData, false);
        }

        /// <summary>
        /// Decodes the DGC data, verifying signature, blacklist and rules if a provider is available.
        /// A result is always returned
        /// </summary>
        /// <param name="qrCodeData">The QRCode data of the DGC</param>
        /// <param name="validationInstant">The instant for the validation of the object (for testing purposes)</param>
        /// <returns></returns>
        public Task<DgcValidationResult> GetValidationResult(string qrCodeData, DateTimeOffset validationInstant)
        {
            return Verify(qrCodeData, validationInstant, false);
        }
        
        #region Private

        /// <summary>
        /// Executes all the decoding steps to get the Cose object from the QR code data
        /// </summary>
        /// <param name="codeData"></param>
        /// <returns></returns>
        /// <exception cref="DgcException"></exception>
        private CoseSign1_Object DecodeCoseObject(string codeData)
        {
            // The base45 encoded data should begin with HC1
            if (codeData.StartsWith("HC1:"))
            {
                string base45CodedData = codeData.Substring(4);

                // Base 45 decode data
                byte[] base45DecodedData = Base45Decoding(Encoding.GetEncoding("UTF-8").GetBytes(base45CodedData));

                // zlib decompression
                byte[] uncompressedData = ZlibDecompression(base45DecodedData);

                // Decoding the COSE_Sign1 object
                var cose = GetCoseObject(uncompressedData);

                return cose;
            }

            throw new DgcException("Invalid data");
        }

        /// <summary>
        /// Verify the signature of the COSE object
        /// </summary>
        /// <param name="cose"></param>
        /// <returns></returns>
        /// <exception cref="DgcSignatureValidationException"></exception>
        private async Task VerifySignature(CoseSign1_Object cose, DateTimeOffset validationInstant)
        {
            try
            {
                var cwt = cose.GetCwt();

                if (cwt == null)
                    throw new DgcSignatureValidationException($"Unable to get Cwt object");

                var issuer = cwt.GetIssuer();
                var issueDate = cwt.GetIssuedAt();
                var expiration = cwt.GetExpiration();

                var kid = cose.GetKeyIdentifier();
                if (kid == null)
                {
                    throw new DgcSignatureValidationException("Signed DGC does not contain kid - cannot find certificate",
                        issuer: issuer, issueDate: issueDate, expirationDate: expiration);
                }
                string kidStr = Convert.ToBase64String(kid);

                // Try by kid and country
                var publicKeyData = await TrustListProvider.GetByKid(kidStr, issuer);

                // If not found, try Kid only
                // Sometimes the issuer of the CBOR is different from the ISO code fo the country
                if (publicKeyData == null)
                    publicKeyData = await TrustListProvider.GetByKid(kidStr);

                if (publicKeyData == null)
                    throw new DgcUnknownSignerException($"No signer certificate could be found for kid {kidStr}", kidStr,
                        issuer, issueDate, expiration);

                try
                {
                    // Checking signature
                    cose.VerifySignature(publicKeyData);

                    // Check signature validity dates
                    if (issueDate != null && issueDate > validationInstant)
                    {
                        throw new DgcSignatureExpiredException($"The signed object is not valid yet",
                            publicKeyData, issuer, issueDate, expiration);
                    }
                    if (expiration == null)
                    {
                        Logger?.LogWarning($"Expiration is not set, assuming is not expired");
                    }
                    else if (expiration < validationInstant)
                    {
                        throw new DgcSignatureExpiredException($"The signed object has expired on {expiration}",
                            publicKeyData, issuer, issueDate, expiration);
                    }

                    Logger?.LogDebug($"HCERT signature verification succeeded using certificate {publicKeyData.Kid}");
                }
                catch (DgcSignatureValidationException e)
                {
                    // Add context information if missing
                    e.Issuer = issuer;
                    e.IssueDate = issueDate;
                    e.ExpirationDate = expiration;

                    Logger?.LogWarning($"HCERT signature verification failed using certificate {publicKeyData.Kid} - {e.Message}");
                    // throw the original exception
                    throw;
                }
            }
            catch (DgcSignatureValidationException) { throw; }
            catch (Exception e)
            {
                // Wrap unmanaged exceptions as DgcSignatureValidationException
                Logger?.LogWarning($"HCERT signature verification failed: {e.Message}");
                throw new DgcSignatureValidationException(e.Message, e);
            }
        }

        /// <summary>
        /// Extract the data from the COSE object
        /// </summary>
        /// <param name="cose"></param>
        /// <returns></returns>
        private SignedDgc GetSignedDgc(CoseSign1_Object cose)
        {
            var cwt = cose.GetCwt();
            if (cwt == null)
                throw new DgcException($"Unable to get Cwt object");

            var dgcData = cwt.GetDgcV1();

            if (dgcData == null)
                throw new DgcException($"Unable to get DGC data from cwt");

            var dgc = GetEuDGCFromCbor(dgcData);
            if (dgc == null)
                throw new DgcException($"Unable to get DGC data from cwt");

            var result = new SignedDgc
            {
                Issuer = cwt.GetIssuer(),
                IssuedDate = cwt.GetIssuedAt(),
                ExpirationDate = cwt.GetExpiration(),
                Dgc = dgc,
            };

            return result;
        }

        private static byte[] Base45Decoding(byte[] encodedData)
        {
            byte[] uncodedData = Base45.Decode(encodedData);
            return uncodedData;
        }

        private static byte[] ZlibDecompression(byte[] compressedData)
        {
            if (compressedData[0] == 0x78)
            {
                var outputStream = new MemoryStream();
                using (var compressedStream = new MemoryStream(compressedData))
                using (var inputStream = new InflaterInputStream(compressedStream))
                {
                    inputStream.CopyTo(outputStream);
                    outputStream.Position = 0;
                    return outputStream.ToArray();
                }
            }
            else
            {
                // The data is not compressed
                return compressedData;
            }
        }

        private static CoseSign1_Object GetCoseObject(byte[] uncompressedData)
        {
            var cose = CoseSign1_Object.Decode(uncompressedData);
            return cose;
        }

        private static EuDGC? GetEuDGCFromCbor(byte[] cborData)
        {
            var cbor = CBORObject.DecodeFromBytes(cborData, CBOREncodeOptions.Default);

            var json = cbor.ToJSONString();
            var vacProof = EuDGC.FromJson(json);

            return vacProof;
        }

        private static string GetDgcResultStatusDescription(DgcResultStatus status)
        {
            switch (status)
            {
                case DgcResultStatus.NotEuDCC:
                    return "Certificate is not a valid EU DGC";
                case DgcResultStatus.InvalidSignature:
                    return "Invalid signature";
                case DgcResultStatus.Blacklisted:
                    return "Certificate is blacklisted";
                case DgcResultStatus.NeedRulesVerification:
                    return "Country rules has not been verified for the certificate";
                case DgcResultStatus.NotValid:
                    return "Certificate is not valid";
                case DgcResultStatus.NotValidYet:
                    return $"Certificate is not valid yet";
                case DgcResultStatus.PartiallyValid:
                    return "Certificate is valid in the country of verification, but may be not valid in other countries";
                case DgcResultStatus.Valid:
                    return "Certificate is valid";
                default:
                    return $"Status {status} not supported";
            }
        }

        #endregion
    }
}
