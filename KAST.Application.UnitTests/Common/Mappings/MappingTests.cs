using AutoMapper;
using KAST.Application.Common.Mappings;
using KAST.Application.Features.Customers.DTOs;
using KAST.Application.Features.Documents.DTOs;
using KAST.Application.Features.KeyValues.DTOs;
using KAST.Application.Features.Products.DTOs;
using KAST.Domain.Entities;
using NUnit.Framework;
using System;
using System.Runtime.Serialization;

namespace KAST.Application.UnitTests.Common.Mappings
{
    public class MappingTests
    {
        private readonly IConfigurationProvider _configuration;
        private readonly IMapper _mapper;

        public MappingTests()
        {
            _configuration = new MapperConfiguration(cfg =>
            {
                //cfg.Advanced.AllowAdditiveTypeMapCreation = true;
                cfg.AddProfile<MappingProfile>();
            });

            _mapper = _configuration.CreateMapper();
        }

        [Test]
        public void ShouldHaveValidConfiguration()
        {
            _configuration.AssertConfigurationIsValid();
        }

        [Test]
        [TestCase(typeof(Customer), typeof(CustomerDto))]
        [TestCase(typeof(Document), typeof(DocumentDto))]
        [TestCase(typeof(KeyValue), typeof(KeyValueDto))]
        [TestCase(typeof(Product), typeof(ProductDto))]
        [TestCase(typeof(ProductDto), typeof(Product))]
        [TestCase(typeof(KeyValueDto), typeof(KeyValue))]
        [TestCase(typeof(DocumentDto), typeof(Document))]
        public void ShouldSupportMappingFromSourceToDestination(Type source, Type destination)
        {
            var instance = GetInstanceOf(source);

            _mapper.Map(instance, source, destination);
        }

        private object GetInstanceOf(Type type)
        {
            if (type.GetConstructor(Type.EmptyTypes) != null)
                return Activator.CreateInstance(type);

            // Type without parameterless constructor
            return FormatterServices.GetUninitializedObject(type);
        }
    }
}