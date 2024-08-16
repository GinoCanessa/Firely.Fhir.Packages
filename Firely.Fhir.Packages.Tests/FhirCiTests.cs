using FluentAssertions;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Specification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

#nullable enable


namespace Firely.Fhir.Packages.Tests
{
    [TestClass]
    public class FhirCiTests
    {
        private FhirCiHttpMessageHandler _ciHttpMessageHandler;
        private HttpClient _httpClient;
        private FhirCiClient _client;

        public FhirCiTests()
        {
            _ciHttpMessageHandler = new();
            _httpClient = new(_ciHttpMessageHandler);
            _client = new FhirCiClient(client: _httpClient);
        }

        [DataTestMethod]
        [DataRow(null, null, null, 359)]                                // 359 distinct packages in qas-full.json
        [DataRow("hl7.fhir.ca.baseline", null, null, 1)]
        [DataRow(null, "http://hl7.org/fhir/ca/baseline", null, 1)]
        [DataRow(null, null, "4.0.1", 293)]                             // 293 distinct packages in qas-full.json with FHIR version 4.0.1
        [DataRow(null, null, "4.3.0", 8)]                               // 8 distinct packages in qas-full.json with FHIR version 4.3.0
        [DataRow(null, null, "5.0.0", 61)]                              // 61 distinct packages in qas-full.json with FHIR version 4.3.0
        public async Task TestFhirCiCatalog(
            string? id, 
            string? canonical,
            string? fhirVersion,
            int expectedCount)
        {
            List<PackageCatalogEntry> entries = await _client.CatalogPackagesAsync(pkgName: id, canonical, fhirVersion);
            entries.Count.Should().Be(expectedCount);

            if (id != null)
            {
                entries.All(e => e.Name == id).Should().BeTrue();
            }
        }
    }

    /// <summary>
    /// Represents an HTTP message handler for FHIR Continuous Integration (CI) tests.
    /// </summary>
    public class FhirCiHttpMessageHandler : HttpMessageHandler
    {
        /// <summary>
        /// Sends an HTTP request as an asynchronous operation.
        /// </summary>
        /// <param name="request">The HTTP request message to send.</param>
        /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            switch (request.RequestUri?.AbsoluteUri)
            {
                // qas.json
                case "http://build.fhir.org/ig/qas.json":
                case "https://build.fhir.org/ig/qas.json":
                    {
                        return Task.FromResult(JsonFile("TestData/ci/qas-full.json"));
                    }

                default:
                    throw new NotImplementedException($"The request URI {request.RequestUri?.AbsoluteUri} is not implemented.");
            }
        }

        /// <summary>
        /// Creates a JSON response message based on the content.
        /// </summary>
        /// <param name="filename">The filename of the JSON file.</param>
        /// <param name="statusCode">(Optional) The status code of the response.</param>
        /// <returns>A HttpResponseMessage object representing the JSON response.</returns>
        internal static HttpResponseMessage JsonFile(
                string filename,
                HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(File.ReadAllText(filename), new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")),
            };
        }

        /// <summary>
        /// Creates an INI response message based on the content.
        /// </summary>
        /// <param name="filename">The filename of the INI file.</param>
        /// <param name="statusCode">(Optional) The status code of the response.</param>
        /// <returns>A HttpResponseMessage object representing the INI response.</returns>
        internal static HttpResponseMessage IniFile(
            string filename,
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(File.ReadAllText(filename), new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain")),
            };
        }

        /// <summary>
        /// Creates a JSON response message based on the content.
        /// </summary>
        /// <param name="content">The content of the JSON response.</param>
        /// <param name="statusCode">(Optional) The status code of the response.</param>
        /// <returns>A HttpResponseMessage object representing the JSON response.</returns>
        internal static HttpResponseMessage JsonContent(
                string content,
                HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")),
            };
        }
    }
}

#nullable restore