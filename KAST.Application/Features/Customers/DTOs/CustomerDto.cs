﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;

namespace KAST.Application.Features.Customers.DTOs
{
    public class CustomerDto : IMapFrom<Customer>
    {
        // TODO: define data transfer object (DTO) fields, for example:
        [Description("Id")]
        public int Id { get; set; }
        [Description("Name")]
        public string? Name { get; set; }
        [Description("Description")]
        public string? Description { get; set; }

    }
}