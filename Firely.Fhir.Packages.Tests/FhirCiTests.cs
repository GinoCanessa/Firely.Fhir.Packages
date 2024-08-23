using FluentAssertions;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
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
        [DataRow(null, null, null, null, null, 445)]                                // 445 distinct packages in qas-full.json
        [DataRow("hl7.fhir.ca.baseline", null, null, null, null, 1)]
        [DataRow(null, null, "http://hl7.org/fhir/ca/baseline/ImplementationGuide/hl7.fhir.ca.baseline", null, null, 1)]
        [DataRow("hl7.fhir.ca.baseline", null, "http://hl7.org/fhir/ca/baseline/ImplementationGuide/hl7.fhir.ca.baseline", null, null, 1)]
        [DataRow("hl7.fhir.ca.baseline", null, null, "HL7-Canada/ca-baseline", null, 1)]
        [DataRow(null, null, null, "HL7-Canada/ca-baseline", null, 1)]
        [DataRow("hl7.fhir.ca.baseline", null, null, "HL7-Canada/ca-baseline/branches/master", null, 1)]
        [DataRow("hl7.fhir.ca.baseline", null, null, null, "master", 1)]
        [DataRow("hl7.fhir.ca.baseline", null, null, null, "main", 0)]
        [DataRow(null, "4.0.1", null, null, null, 345)]                             // 345 distinct packages in qas-full.json with FHIR version 4.0.1
        [DataRow(null, "4.3.0", null, null, null, 13)]                              // 13 distinct packages in qas-full.json with FHIR version 4.3.0
        [DataRow(null, "5.0.0", null, null, null, 91)]                              // 91 distinct packages in qas-full.json with FHIR version 5.0.0
        public async Task TestFhirCiCatalog(
            string? id,
            string? fhirVersion,
            string? siteUrl,
            string? repo,
            string? branch,
            int expectedCount)
        {
            List<PackageCatalogEntry> entries = await _client.CatalogPackagesAsync(
                pkgname: id, 
                fhirversion: fhirVersion,
                site: siteUrl,
                repo: repo,
                branch: branch);
            entries.Count.Should().Be(expectedCount);

            if (id != null)
            {
                entries.All(e => e.Name == id).Should().BeTrue();
            }
        }

        [DataTestMethod]
        [DataRow("hl7.fhir.ca.baseline", 2)]
        [DataRow("cinc.fhir.ig", 9)]
        public async Task TestFhirCiDownloadListing(
            string id,
            int expectedCount)
        {
            PackageListing? listing = await _client.DownloadListingAsync(id);

            if (expectedCount == 0)
            {
                listing.Should().BeNull();
                return;
            }

            listing.Should().NotBeNull();
            if (listing == null)
            {
                return;
            }

            listing.Versions.Should().NotBeNull();
            if (listing.Versions == null)
            {
                return;
            }

            listing.Versions.Count.Should().Be(expectedCount);

            listing.Versions.All(e => e.Value.Name == id).Should().BeTrue();
        }

        [DataTestMethod]
        [DataRow("hl7.fhir.ca.baseline", 2)]
        [DataRow("cinc.fhir.ig", 9)]
        [DataRow("ihe.pcc.qedm", 4)]
        public async Task TestFhirCiGetVersions(
            string id,
            int expectedCount)
        {
            Versions? versions = await _client.GetVersions(id);

            versions.Should().NotBeNull();
            if (versions == null)
            {
                return;
            }

            if (expectedCount == 0)
            {
                versions.Items.Count.Should().Be(0);
                return;
            }

            versions.Items.Count.Should().Be(expectedCount);
            versions.Items.All(e => e.ToString().Contains("-")).Should().BeTrue();
        }


        [DataTestMethod]
        [DataRow("hl7.fhir.ca.baseline",    null,                           "1.1.0-cibuild+20240809-194642Z")]
        [DataRow("hl7.fhir.ca.baseline",    "current",                      "1.1.0-cibuild+20240809-194642Z")]
        [DataRow("hl7.fhir.ca.baseline",    "master",                       "1.1.0-cibuild+20240809-194642Z")]
        [DataRow("hl7.fhir.ca.baseline",    "current$master",               "1.1.0-cibuild+20240809-194642Z")]
        [DataRow("cinc.fhir.ig",            null,                           "0.4.0-cibuild+20240702-012714Z")]
        [DataRow("cinc.fhir.ig",            "current",                      "0.4.0-cibuild+20240702-012714Z")]
        [DataRow("cinc.fhir.ig",            "master",                       "0.4.0-cibuild+20240702-012714Z")]
        [DataRow("cinc.fhir.ig",            "current$master",               "0.4.0-cibuild+20240702-012714Z")]
        [DataRow("cinc.fhir.ig",            "CommunicationPerson",          "0.4.0-cibuild+20240627-051754Z")]
        [DataRow("cinc.fhir.ig",            "RFphase1",                     "0.3.9-cibuild+20240618-041305Z")]
        [DataRow("ihe.pcc.qedm",            null,                           "3.0.0-comment1+20240805-120740Z")]
        public async Task TestFhirCiResolve(
            string id,
            string? versionDiscriminator,
            string expectedVersion)
        {
            (PackageReference tagged, PackageReference resolved) = await _client.GetReferences(id, versionDiscriminator);

            if (string.IsNullOrEmpty(expectedVersion))
            {
                tagged.Should().BeEquivalentTo(PackageReference.None);
                resolved.Should().BeEquivalentTo(PackageReference.None);
                return;
            }

            tagged.Name.Should().BeEquivalentTo(id);
            if (string.IsNullOrEmpty(versionDiscriminator) || (versionDiscriminator == "current"))
            {
                tagged.Version.Should().BeEquivalentTo("current");
            }
            else if (versionDiscriminator.Contains('$'))
            {
                tagged.Version.Should().BeEquivalentTo(versionDiscriminator);
            }
            else
            {
                tagged.Version.Should().BeEquivalentTo($"current${versionDiscriminator}");
            }

            resolved.Name.Should().BeEquivalentTo(id);
            resolved.Version.Should().BeEquivalentTo(expectedVersion);
        }


        [DataTestMethod]
        [DataRow("not-a-real-package",   null,                               true)]
        [DataRow("cinc.fhir.ig",         null,                               false)]
        [DataRow("cinc.fhir.ig",         null,                               false)]
        [DataRow("cinc.fhir.ig",         "0.4.0-cibuild+20240702-012714Z",   false)]
        [DataRow("cinc.fhir.ig",         "current",                          false)]
        [DataRow("cinc.fhir.ig",         "0.3.9-cibuild+20240618-041305Z",   false)]
        [DataRow("cinc.fhir.ig",         "current$RFphase1",                 false)]
        [DataRow("ihe.pcc.qedm",         "3.0.0-comment1+20240805-120740Z",  false)]
        [DataRow("ihe.pcc.qedm",         "current",                          false)]
        public async Task TestFhirCiDownloadPackage(
            string name,
            string? version,
            bool shouldThrow)
        {
            bool threw = false;
            string message = string.Empty;

            try
            {
                (PackageReference tagged, PackageReference resolved) = await _client.GetReferences(name, version);
                byte[] packageData = await _client.GetPackage(resolved);
            }
            catch (Exception ex)
            {
                message = ex.InnerException == null ? ex.Message : ex.Message + ex.InnerException.Message;
                threw = true;
            }

            threw.Should().Be(shouldThrow, message);
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

                // package downloads for cinc.fhir.ig
                case "https://build.fhir.org/ig/tewhatuora/cinc-fhir-ig/package.tgz":
                case "https://build.fhir.org/ig/tewhatuora/cinc-fhir-ig/branches/master/package.tgz":
                case "https://build.fhir.org/ig/tewhatuora/cinc-fhir-ig/branches/RFphase1/package.tgz":
                // package downloads for ihe.pcc.qedm
                case "https://profiles.ihe.net/PCC/QEDm/package.tgz":
                case "https://profiles.ihe.net/PCC/QEDm/branches/master/package.tgz":
                    {
                        return Task.FromResult(EmptyResponse("application/gzip"));
                    }

                default:
                    throw new NotImplementedException($"The request URI {request.RequestUri?.AbsoluteUri} is not implemented.");
            }
        }

        internal static HttpResponseMessage EmptyResponse(
            string mimeType = "text/plain",
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(string.Empty, new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType)),
            };
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