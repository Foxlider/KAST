// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace KAST.Application.Common.Models
{
    public abstract class PaginationRequest
    {
        public string? FilterRules { get; set; }
        public int Page { get; set; } = 1;
        public int Rows { get; set; } = 15;
        public string Sort { get; set; } = "Id";
        public string Order { get; set; } = "desc";
        public override string ToString() => $"page:{Page},rows:{Rows},sort:{Sort},order:{Order},filterRule:{FilterRules}";
    }
}