/* 
 * Copyright (c) 2022, Firely (info@fire.ly) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://github.com/FirelyTeam/Firely.Fhir.Packages/blob/master/LICENSE
 */


#nullable enable

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Firely.Fhir.Packages
{
    /// <summary>
    /// Represents a client for interacting with a FHIR Continuous Integration (CI) server.
    /// </summary>
    public class FhirCiClient : IPackageServer, IDisposable
    {
        /// <summary>(Immutable) The default time to refresh the CI build listings.</summary>
        private const int _defaultInvalidationSeconds = 60 * 10;

        /// <summary>(Immutable) URI of the FHIR CI server.</summary>
        private static readonly Uri _ciUri = new("http://build.fhir.org/");

        /// <summary>(Immutable) The ci URI using HTTPS.</summary>
        private static readonly Uri _ciUriS = new("https://build.fhir.org/");

        /// <summary>(Immutable) URI of the qas.</summary>
        private static readonly Uri _qasUri = new("https://build.fhir.org/ig/qas.json");

        /// <summary>(Immutable) URI of the ig list.</summary>
        private static readonly Uri _igListUri = new("https://github.com/FHIR/ig-registry/blob/master/fhir-ig-list.json");

        /// <summary>(Immutable) The HTTP client.</summary>
        private readonly HttpClient _httpClient;

        /// <summary>The QAS records for the CI server, grouped by package id.</summary>
        private Dictionary<string, List<FhirCiQaRecord>> _qasByPackageId = new();

        /// <summary>The QAS records for the CI server.</summary>
        private FhirCiQaRecord[] _qas = Array.Empty<FhirCiQaRecord>();

        /// <summary>When the local copy of the CI QAS was last updated.</summary>
        private DateTimeOffset _qasLastUpdated = DateTimeOffset.MinValue;

        /// <summary>The listing invalidation in seconds.</summary>
        private int _listingInvalidationSeconds;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirCiClient"/> class.
        /// </summary>
        /// <param name="listingInvalidationSeconds">(Optional) The listing invalidation in seconds.</param>
        /// <param name="client">(Optional) The <see cref="HttpClient"/> instance to use. If null, a new instance will be created.</param>
        public FhirCiClient(int listingInvalidationSeconds = _defaultInvalidationSeconds, HttpClient? client = null)
        {
            _httpClient = client ?? new HttpClient();
        }

        /// <summary>
        /// Creates a new instance of the <see cref="FhirCiClient"/> class.
        /// </summary>
        /// <param name="listingInvalidationSeconds">(Optional) The listing invalidation in seconds.</param>
        /// <param name="insecure">(Optional) Specifies whether to create an insecure client.</param>
        /// <returns>A new instance of the <see cref="FhirCiClient"/> class.</returns>
        public static FhirCiClient Create(int listingInvalidationSeconds = _defaultInvalidationSeconds, bool insecure = false)
        {
            var httpClient = insecure ? Testing.GetInsecureClient() : new HttpClient();
            return new FhirCiClient(listingInvalidationSeconds, httpClient);
        }

        /// <inheritdoc/>
        public override string? ToString() => _ciUriS.ToString();

        private async Task updateQasIfNeeded()
        {
            if (_qasLastUpdated.AddSeconds(_listingInvalidationSeconds) < DateTimeOffset.Now)
            {
                FhirCiQaRecord[]? updatedQas = await getQas();
                if (updatedQas == null)
                {
                    return;
                }

                Dictionary<string, List<FhirCiQaRecord>> qasByPackageId = new();

                // iterate over the QAS records and add them to a dictionary
                foreach (FhirCiQaRecord qas in updatedQas)
                {
                    if (qas.PackageId == null)
                    {
                        continue;
                    }

                    if (!qasByPackageId.TryGetValue(qas.PackageId, out List<FhirCiQaRecord>? qasRecs))
                    {
                        qasRecs = new List<FhirCiQaRecord>();
                        qasByPackageId.Add(qas.PackageId, qasRecs);
                    }

                    qasRecs.Add(qas);
                }

                // swap our dictionaries
                _qas = updatedQas;
                _qasByPackageId = qasByPackageId;
                _qasLastUpdated = DateTimeOffset.Now;
            }
        }

        private async Task<FhirCiQaRecord[]?> getQas()
        {
            try
            {
                string contents = await _httpClient.GetStringAsync(_qasUri);

                return JsonConvert.DeserializeObject<FhirCiQaRecord[]>(contents);
            }
            finally
            {
            }
        }

        /// <summary>
        /// Download the raw json package listing.
        /// </summary>
        /// <param name="pkgName">Name of the package.</param>
        /// <returns>Raw package listing in json format.</returns>
        public async ValueTask<string?> DownloadListingRawAsync(string? pkgName)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Download the package listing.
        /// </summary>
        /// <param name="pkgName">Name of the package.</param>
        /// <returns>Package listing.</returns>
        public async ValueTask<PackageListing?> DownloadListingAsync(string pkgName)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Get a list of package catalogs, based on optional parameters.
        /// </summary>
        /// <param name="pkgName">Name of the package.</param>
        /// <param name="canonical">The canonical url of an artifact that is in the package.</param>
        /// <param name="fhirVersion">The FHIR version of a package.</param>
        /// <param name="preview">Allow for prelease packages.</param>
        /// <returns>A list of package catalogs that conform to the parameters.</returns>
        public async ValueTask<List<PackageCatalogEntry>> CatalogPackagesAsync(
            string? pkgName = null,
            string? canonical = null,
            string? fhirVersion = null,
            bool preview = true)
        {
            List<PackageCatalogEntry> entries = new();

            await updateQasIfNeeded();

            HashSet<string> usedIds = new();

            // if there was a package name provided, we can use our dictionary for lookup
            if (pkgName != null)
            {
                if (!_qasByPackageId.TryGetValue(pkgName, out List<FhirCiQaRecord>? qasRecs))
                {
                    return entries;
                }
                
                // iterate over the records in our dictionary
                foreach (FhirCiQaRecord qas in qasRecs)
                {
                    int igUrlIndex = qas.Url?.IndexOf("/ImplementationGuide/", StringComparison.Ordinal) ?? -1;
                    string? qasCanonical = igUrlIndex == -1 ? qas.Url : qas.Url!.Substring(0, igUrlIndex);

                    if ((fhirVersion != null) && (qas.FhirVersion != fhirVersion))
                    {
                        continue;
                    }

                    if ((canonical != null) && (qasCanonical != canonical))
                    {
                        continue;
                    }

                    if (usedIds.Contains(qas.PackageId ?? string.Empty))
                    {
                        continue;
                    }
                    usedIds.Add(qas.PackageId ?? string.Empty);

                    entries.Add(new PackageCatalogEntry()
                    {
                        Name = qas.PackageId,
                        Description = qas.Name,
                        FhirVersion = qas.FhirVersion,
                    });
                }

                return entries;
            }

            // iterate over all the QAS records
            foreach (FhirCiQaRecord qas in _qas)
            {
                int igUrlIndex = qas.Url?.IndexOf("/ImplementationGuide/", StringComparison.Ordinal) ?? -1;
                string? qasCanonical = igUrlIndex == -1 ? qas.Url : qas.Url!.Substring(0, igUrlIndex);

                if ((fhirVersion != null) && (qas.FhirVersion != fhirVersion))
                {
                    continue;
                }

                if ((canonical != null) && (qasCanonical != canonical))
                {
                    continue;
                }

                if (usedIds.Contains(qas.PackageId ?? string.Empty))
                {
                    continue;
                }
                usedIds.Add(qas.PackageId ?? string.Empty);

                entries.Add(new PackageCatalogEntry()
                {
                    Name = qas.PackageId,
                    Description = qas.Name,
                    FhirVersion = qas.FhirVersion,
                });
            }

            return entries;
        }

        /// <summary>
        /// Downloads a package from the source.
        /// </summary>
        /// <param name="reference">Package reference of the package to be downloaded.</param>
        /// <returns>Package content as a byte array.</returns>
        private async ValueTask<byte[]> downloadPackage(PackageReference reference)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Publish a package to the package source.
        /// </summary>
        /// <param name="reference">PackageReference of the package to be published.</param>
        /// <param name="fhirRelease">FHIR Release that is used by the package.</param>
        /// <param name="buffer">Package content.</param>
        /// <returns>Http response whether the package has been successfully published.</returns>
        public async ValueTask<HttpResponseMessage> Publish(PackageReference reference, byte[] buffer)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Retrieve the different versions of a package.
        /// </summary>
        /// <param name="name">Package name.</param>
        /// <returns>List of versions.</returns>
        public async Task<Versions?> GetVersions(string name)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Download a package from the source.
        /// </summary>
        /// <param name="reference">Package reference of the package to be downloaded.</param>
        /// <returns>Package content as byte array.</returns>
        public async Task<byte[]> GetPackage(PackageReference reference)
        {
            throw new NotImplementedException();
        }

        #region IDisposable

        private bool _disposed;

        /// <inheritdoc/>
        void IDisposable.Dispose() => Dispose(true);

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="FhirCiClient"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // [WMR 20181102] HttpClient will dispose internal HttpClientHandler/WebRequestHandler
                    _httpClient?.Dispose();
                }

                // release any unmanaged objects
                // set the object references to null
                _disposed = true;
            }
        }

        #endregion
    }
}

#nullable restore