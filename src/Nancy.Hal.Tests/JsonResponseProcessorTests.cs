﻿using System.Linq;
using Nancy.Hal.Configuration;
using Nancy.Hal.Processors;
using Nancy.Serialization.JsonNet;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using ServiceStack.Text;
using Xunit;
using System;
using System.IO;
using Nancy.Responses;
using Nancy.Responses.Negotiation;
using Nancy.Serializers.Json.ServiceStack;
using Newtonsoft.Json.Linq;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace Nancy.Hal.Tests
{

    public abstract class JsonResponseProcessorTests
    {
        [Fact]
        public void ShouldBuildStaticLinks()
        {
            var config = new HalConfiguration();
            config.For<PetOwner>().
                Links("rel1", "/staticAddress1").
                Links(new Link("rel2", "/staticAddress2"));
            
            var json = Serialize(new PetOwner {Name = "Bob"}, config);

            Assert.Equal("Bob", GetStringValue(json, "Name"));
            Assert.Equal("/staticAddress1", GetStringValue(json, "_links", "rel1", "href"));
            Assert.Equal("/staticAddress2", GetStringValue(json, "_links", "rel2", "href"));
        }

        [Fact]
        public void ShouldBuildDynamicLinks()
        {
            var config = new HalConfiguration();
            config.For<PetOwner>().
                Links(model => new Link("rel1", "/dynamic/{name}").CreateLink(model)).
                Links((model, ctx) => new Link("rel2", "/dynamic/{name}/{operation}").CreateLink(model, ctx.Request.Query));

            var json = Serialize(new PetOwner { Name = "Bob" }, config, CreateTestContext(new { Operation = "Duck" }));

            Assert.Equal("/dynamic/Bob", GetStringValue(json, "_links", "rel1", "href"));
            Assert.Equal("/dynamic/Bob/Duck", GetStringValue(json, "_links", "rel2", "href"));
        }

        [Fact]
        public void ShouldBuildMultipleLinksForSingleRel()
        {
            var config = new HalConfiguration();
            config.For<PetOwner>().
                Links(new Link("rel1", "/static1")).
                Links(new Link("rel1", "/static2")).
                Links(model => new Link("rel2", "/dynamic/{name}").CreateLink(model)).
                Links((model, ctx) => new Link("rel2", "/dynamic/{name}/{operation}").CreateLink(model, ctx.Request.Query));

            var json = Serialize(new PetOwner { Name = "Bob" }, config, CreateTestContext(new { Operation = "Duck" }));

            var rel1Links = GetData(json, "_links", "rel1");
            Assert.Equal(rel1Links.Count(), 2);
            Assert.Equal(new[] { "/static1", "/static2" }, rel1Links.Select(token => token["href"].ToString()));
            var rel2Links = GetData(json, "_links", "rel2");
            Assert.Equal(rel2Links.Count(), 2);
            Assert.Equal(new[] { "/dynamic/Bob", "/dynamic/Bob/Duck" }, rel2Links.Select(token => token["href"].ToString()));
        }

        [Fact]
        public void ShouldBuildDynamicLinksWithPredicates()
        {
            var config = new HalConfiguration();
            config.For<PetOwner>().
                Links(model => new Link("rel1", "/dynamic/on/{name}").CreateLink(model), model => model.Happy).
                Links(model => new Link("rel2", "/dynamic/off/{name}").CreateLink(model), (model, ctx) => !model.Happy).
                Links((model, ctx) => new Link("rel3", "/dynamic/on/{name}/{operation}").CreateLink(model, ctx.Request.Query), model => model.Happy).
                Links((model, ctx) => new Link("rel4", "/dynamic/off/{name}/{operation}").CreateLink(model, ctx.Request.Query), (model, ctx) => !model.Happy);

            var json = Serialize(new PetOwner { Name = "Bob", Happy = true }, config, CreateTestContext(new { Operation = "Duck" }));

            Assert.Equal("/dynamic/on/Bob", GetStringValue(json, "_links", "rel1", "href"));
            Assert.Null(GetStringValue(json, "_links", "rel2", "href"));
            Assert.Equal("/dynamic/on/Bob/Duck", GetStringValue(json, "_links", "rel3", "href"));
            Assert.Null(GetStringValue(json, "_links", "rel4", "href"));
        }

        [Fact]
        public void ShouldEmbedSubResources()
        {
            var config = new HalConfiguration();
            config.For<PetOwner>().
                Embeds("pampered", owner => owner.Pets).
                Embeds(owner => owner.LiveStock);

            var model = new PetOwner
            {
                Name = "Bob",
                Happy = true,
                Pets = new[] { new Animal { Type = "Cat" } },
                LiveStock = new Animal { Type = "Chicken" }
            };
            var json = Serialize(model, config);
             
            Assert.Equal("Cat", GetData(json, "_embedded", "pampered")[0][AdjustName("Type")]);
            Assert.Equal("Chicken", GetStringValue(json, "_embedded", "liveStock", "Type"));
        }

        [Fact]
        public void ShouldEmbedSubResourceProjections()
        {
            var config = new HalConfiguration();
            config.For<PetOwner>().
                Projects("pampered", owner => owner.Pets, pets => new {petCount=pets.Count()}).
                Projects(owner => owner.LiveStock, stock => new {stockType=stock.Type});

            var model = new PetOwner
            {
                Name = "Bob",
                Happy = true,
                Pets = new[] { new Animal { Type = "Cat" } },
                LiveStock = new Animal { Type = "Chicken" }
            };
            var json = Serialize(model, config, CreateTestContext(new{Operation="Duck"}));

            Assert.Equal("1", GetData(json, "_embedded", "pampered", "petCount"));
            Assert.Equal("Chicken", GetStringValue(json, "_embedded", "liveStock", "stockType"));
        }

        private object GetStringValue(JToken json, params string[] names)
        {
            var data = GetData(json, names);
            return data!=null ? data.ToString() : null;
        }

        private JToken GetData(JToken json, params string[] names)
        {
            return names.Aggregate(json, (current, name) => current!=null ? current[AdjustName(name)] : null);
        }

        protected virtual string AdjustName(string name)
        {
            return name;
        }

        private static NancyContext CreateTestContext(dynamic query)
        {
            var context = new NancyContext { Request = new Request("method", "path", "http") { Query = query } };
            return context;
        }

        protected abstract ISerializer JsonSerializer { get; }

        private JObject Serialize(object model, IProvideHalTypeConfiguration config, NancyContext context = null)
        {
            if (context == null) context = new NancyContext();

            var processor = new HalJsonResponseProcessor(config, new[] { JsonSerializer });
            var response = (JsonResponse)processor.Process(new MediaRange("application/hal+json"), model, context);
            var stream = new MemoryStream();
            response.Contents.Invoke(stream);
            stream.Seek(0, SeekOrigin.Begin);
            var text = new StreamReader(stream).ReadToEnd();

            Console.WriteLine(text);
            return JObject.Parse(text);
        }
    }

    public class DefaultJsonSerializerTests : JsonResponseProcessorTests
    {
        protected override ISerializer JsonSerializer
        {
            get { return new DefaultJsonSerializer(); }
        }

        // Serialiser converts names to camel case
        protected override string AdjustName(string name)
        {
            return name.ToCamelCase();
        }
    }

    public class JsonNetSerializerTests : JsonResponseProcessorTests
    {
        protected override ISerializer JsonSerializer
        {
            get
            {
                return
                    new JsonNetSerializer(
                        new JsonSerializer
                            {
                                NullValueHandling = NullValueHandling.Ignore,
                                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                                Formatting = Formatting.Indented
                            });
            }
        }

        // Serialiser converts names to camel case
        protected override string AdjustName(string name)
        {
            return name.ToCamelCase();
        }
    }

    public class ServiceStackSerializerTests : JsonResponseProcessorTests
    {
        //doesnt work because ServiceStackJsonSerializer won't let me override camelcase settings
        //(need to expose a constructor so i can pass my own instance in)
        protected override ISerializer JsonSerializer
        {
            get
            {
                return new ServiceStackJsonSerializer();
            }
        }
    }
}
