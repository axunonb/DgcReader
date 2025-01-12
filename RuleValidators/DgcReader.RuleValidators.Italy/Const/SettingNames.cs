﻿// Copyright (c) 2021 Davide Trevisan
// Licensed under the Apache License, Version 2.0

namespace DgcReader.RuleValidators.Italy.Const
{
    /// <summary>
    /// Setting names supported by the validator
    /// </summary>
    public static class SettingNames
    {
        public const string VaccineEndDayComplete = "vaccine_end_day_complete";
        public const string VaccineStartDayComplete = "vaccine_start_day_complete";
        public const string VaccineEndDayNotComplete = "vaccine_end_day_not_complete";
        public const string VaccineStartDayNotComplete = "vaccine_start_day_not_complete";

        public const string RapidTestStartHours = "rapid_test_start_hours";
        public const string RapidTestEndHours = "rapid_test_end_hours";
        public const string MolecularTestStartHours = "molecular_test_start_hours";
        public const string MolecularTestEndHours = "molecular_test_end_hours";

        public const string RecoveryCertStartDay = "recovery_cert_start_day";
        public const string RecoveryCertEndDay = "recovery_cert_end_day";

        public const string AndroidAppMinVersion = "android";
        public const string SdkMinVersion = "sdk";

        public const string Blacklist = "black_list_uvci";
    }
}
