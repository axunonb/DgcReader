﻿#if NETSTANDARD2_0_OR_GREATER || NET5_0_OR_GREATER || NET47_OR_GREATER
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;

// Copyright (c) 2021 Davide Trevisan
// Licensed under the Apache License, Version 2.0

namespace DgcReader.TrustListProviders.Sweden
{
    public class SwedishTrustListProviderBuilder
    {
        private IServiceCollection Services { get; }
        public SwedishTrustListProviderBuilder(IServiceCollection services)
        {
            Services = services;
            
            Services.AddHttpClient();

            Services.RemoveAll<ITrustListProvider>();
            Services.AddSingleton<ITrustListProvider, SwedishTrustListProvider>();
        }

        public SwedishTrustListProviderBuilder Configure(Action<SwedishTrustListProviderOptions> configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            Services.Configure(configuration);

            return this;
        }
    }
}


#endif