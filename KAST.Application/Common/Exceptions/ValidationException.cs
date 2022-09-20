// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentValidation.Results;

namespace KAST.Application.Common.Exceptions
{
    public class ValidationException : CustomException
    {
        public ValidationException(IEnumerable<ValidationFailure> failures) : base(string.Empty, failures
                 .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
                 .Select(failureGroup => $"{string.Join(", ", failureGroup.Distinct().ToArray())}")
                 .ToList(), System.Net.HttpStatusCode.UnprocessableEntity)

        {

        }

    }
}