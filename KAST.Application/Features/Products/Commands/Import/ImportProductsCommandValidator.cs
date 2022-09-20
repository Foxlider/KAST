// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace KAST.Application.Features.Products.Commands.Import
{
    public class ImportProductsCommandValidator : AbstractValidator<ImportProductsCommand>
    {
        public ImportProductsCommandValidator()
        {

            RuleFor(v => v.Data)
                  .NotNull()
                 .NotEmpty();

        }
    }
}