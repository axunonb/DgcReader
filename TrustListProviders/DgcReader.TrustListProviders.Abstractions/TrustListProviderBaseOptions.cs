﻿using DgcReader.TrustListProviders.Abstractions.Interfaces;
using System;

// Copyright (c) 2021 Davide Trevisan
// Licensed under the Apache License, Version 2.0

namespace DgcReader.TrustListProviders.Abstractions
{
    /// <inheritdoc cref="ITrustListProviderBaseOptions"/>
    public class TrustListProviderBaseOptions : ITrustListProviderBaseOptions
    {
        /// <inheritdoc />
        public bool UseAvailableListWhileRefreshing { get; set; } = true;

        /// <inheritdoc />
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(24);

        /// <inheritdoc />
        public TimeSpan MinRefreshInterval { get; set; } = TimeSpan.FromHours(1);
    }
}
