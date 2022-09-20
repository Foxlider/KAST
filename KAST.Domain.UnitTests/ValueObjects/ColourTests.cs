// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using KAST.Domain.Exceptions;
using KAST.Domain.ValueObjects;
using NUnit.Framework;

namespace KAST.Domain.UnitTests.ValueObjects
{
    public class ColourTests
    {
        [Test]
        public void ShouldReturnCorrectColourCode()
        {
            var code = "#FFFFFF";

            var colour = Colour.From(code);

            colour.Code.Should().Be(code);
        }

        [Test]
        public void ToStringReturnsCode()
        {
            var colour = Colour.White;

            colour.ToString().Should().Be(colour.Code);
        }

        [Test]
        public void ShouldPerformImplicitConversionToColourCodeString()
        {
            string code = Colour.White;

            code.Should().Be("#FFFFFF");
        }

        [Test]
        public void ShouldPerformExplicitConversionGivenSupportedColourCode()
        {
            var colour = (Colour)"#FFFFFF";

            colour.Should().Be(Colour.White);
        }

        [Test]
        public void ShouldThrowUnsupportedColourExceptionGivenNotSupportedColourCode()
        {
            FluentActions.Invoking(() => Colour.From("##FF33CC"))
                .Should().Throw<UnsupportedColourException>();
        }
    }
}