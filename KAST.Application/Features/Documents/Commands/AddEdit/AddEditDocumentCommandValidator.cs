// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace KAST.Application.Features.Documents.Commands.AddEdit
{
    public class AddEditDocumentCommandValidator : AbstractValidator<AddEditDocumentCommand>
    {
        public AddEditDocumentCommandValidator()
        {
            RuleFor(v => v.Title)
                .NotNull()
                .MaximumLength(256)
                .NotEmpty();
            RuleFor(v => v.DocumentType)
                .NotNull();
            RuleFor(v => v.Description)
                .MaximumLength(256);
            RuleFor(v => v.UploadRequest)
                .NotNull()
                .When(x => x.Id <= 0);


        }
        public Func<object, string, Task<IEnumerable<string>>> ValidateValue => async (model, propertyName) =>
        {
            var result = await ValidateAsync(ValidationContext<AddEditDocumentCommand>.CreateWithOptions((AddEditDocumentCommand)model, x => x.IncludeProperties(propertyName)));
            if (result.IsValid)
                return Array.Empty<string>();
            return result.Errors.Select(e => e.ErrorMessage);
        };
    }
}