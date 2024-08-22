/* 
 * Copyright (c) 2022, Firely (info@fire.ly) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://github.com/FirelyTeam/Firely.Fhir.Packages/blob/master/LICENSE
 */


#nullable enable

using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Newtonsoft.Json;
using SemanticVersioning;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Firely.Fhir.Packages
{
    /// <summary>
    /// Represents a client for interacting with a FHIR Continuous Integration (CI) server.
    /// </summary>
    public class FhirCiClient : IPackageServer, IDisposable
    {
        /// <summary>(Immutable) The ci scope.</summary>
        public const string FhirCiScope = "build.fhir.org";

        /// <summary>(Immutable) The default time to refresh the CI build listings, 0 for every request (no caching), -1 to never automatically refresh.</summary>
        private const int _defaultInvalidationSeconds = -1;

        /// <summary>(Immutable) Literal used to designate the branch name in the version string.</summary>
        private const string _ciBranchDelimiter = ".b-";

        /// <summary>(Immutable) The CI version date format.</summary>
        private const string _ciVersionDateFormat = "yyyyMMdd-HHmmssZ";

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

        /// <summary>Initializes a new instance of the <see cref="FhirCiClient"/> class.</summary>
        /// <param name="listingInvalidationSeconds">(Optional) The listing invalidation in seconds, 0 for
        ///  every request (no caching), -1 to never automatically refresh.</param>
        /// <param name="client">                    (Optional) The <see cref="HttpClient"/> instance to
        ///  use. If null, a new instance will be created.</param>
        public FhirCiClient(int listingInvalidationSeconds = _defaultInvalidationSeconds, HttpClient? client = null)
        {
            _listingInvalidationSeconds = listingInvalidationSeconds;
            _httpClient = client ?? new HttpClient();
        }

        /// <summary>Creates a new instance of the <see cref="FhirCiClient"/> class.</summary>
        /// <param name="listingInvalidationSeconds">(Optional) The listing invalidation in seconds, 0 for
        ///  every request (no caching), -1 to never automatically refresh.</param>
        /// <param name="insecure">                  (Optional) Specifies whether to create an insecure
        ///  client.</param>
        /// <returns>A new instance of the <see cref="FhirCiClient"/> class.</returns>
        public static FhirCiClient Create(int listingInvalidationSeconds = _defaultInvalidationSeconds, bool insecure = false)
        {
            var httpClient = insecure ? Testing.GetInsecureClient() : new HttpClient();
            return new FhirCiClient(listingInvalidationSeconds, httpClient);
        }

        /// <inheritdoc/>
        public override string? ToString() => _ciUriS.ToString();

        /// <summary>Updates the ci listing cache.</summary>
        /// <returns>An asynchronous result.</returns>
        public async Task UpdateCiListingCache()
        {
            _ = await getQAs(true);
        }

        /// <summary>Get the QA records and update the cache if necessary.</summary>
        /// <param name="forceRefresh">(Optional) True to force a refresh.</param>
        /// <returns>An asynchronous result that yields a list of.</returns>
        private async Task<(FhirCiQaRecord[] qas, Dictionary<string, List<FhirCiQaRecord>> qasByPackageId)> getQAs(bool forceRefresh = false)
        {
            // check for having a cached copy and configuration to never refresh the cache
            if (!forceRefresh && (_listingInvalidationSeconds == -1) && (_qas.Length != 0))
            {
                // return what we have
                return (_qas, _qasByPackageId);
            }

            // check for having a cached copy and not needing to refresh
            if (!forceRefresh &&
                (_listingInvalidationSeconds > 0) &&
                (_qasLastUpdated.AddSeconds(_listingInvalidationSeconds) >= DateTimeOffset.Now))
            {
                // return what we have
                return (_qas, _qasByPackageId);
            }

            FhirCiQaRecord[]? updatedQas = await downloadCurrentQAs();
            if (updatedQas == null)
            {
                return (_qas, _qasByPackageId);
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

            // update our cache if necessary
            if (forceRefresh || (_listingInvalidationSeconds != 0))
            {
                _qas = updatedQas;
                _qasByPackageId = qasByPackageId;
                _qasLastUpdated = DateTimeOffset.Now;

                // return our updated version
                return (_qas, _qasByPackageId);
            }

            // return what we downloaded
            return (updatedQas, qasByPackageId);
        }

        private async Task<FhirCiQaRecord[]?> downloadCurrentQAs()
        {
            string contents = await _httpClient.GetStringAsync(_qasUri);
            return JsonConvert.DeserializeObject<FhirCiQaRecord[]>(contents);
        }

        private static string buildVersionString(FhirCiQaRecord qa)
        {
            string versionPrerelease = qa.PackageVersion?.Contains('-') ?? false
                ? string.Empty
                : "-cibuild";

            // prefer the date of the build as the build metadata
            string? buildMeta = qa.BuildDateIso?.ToUniversalTime().ToString(_ciVersionDateFormat)
                ?? qa.BuildDate?.ToUniversalTime().ToString(_ciVersionDateFormat);

            // if we do not have a date, mangle the branch info
            if (buildMeta == null)
            {
                string? branchName = getBranchFromRepo(qa.RepositoryUrl);

                versionPrerelease = string.IsNullOrEmpty(branchName)
                    ? versionPrerelease
                    : (versionPrerelease + _ciBranchDelimiter + cleanForSemVer(branchName!));

                // add the repo portions to the version
                string[] repoComponents = qa.RepositoryUrl?.Split('/') ?? Array.Empty<string>();
                if (repoComponents.Length > 2)
                {
                    buildMeta = repoComponents[0] + "." + repoComponents[1];
                }
                else
                {
                    buildMeta = "ci";
                }
            }

            string packageVersion = qa.PackageVersion ?? "0.0.0";

            return $"{packageVersion}{versionPrerelease}+{cleanForSemVer(buildMeta)}";
        }

        private PackageListing? listingFromQaRecs(List<FhirCiQaRecord> qaRecs)
        {
            PackageListing? listing = null;

            // iterate over the records in our dictionary - sort by status so that 'active' comes before 'retired'
            foreach (FhirCiQaRecord qa in qaRecs.OrderBy(qa => qa.Status))
            {
                listing ??= new()
                {
                    Id = qa.PackageId,
                    Name = qa.Name,
                    Description = $"CI Build of {qa.PackageId}",
                    DistTags = null,
                    Versions = new(),
                };

                int igUrlIndex = qa.Url?.IndexOf("/ImplementationGuide/", StringComparison.Ordinal) ?? -1;
                string? qaUrl = igUrlIndex == -1 ? qa.Url : qa.Url!.Substring(0, igUrlIndex);

                string versionLiteral = buildVersionString(qa);

                if (listing.Versions!.ContainsKey(versionLiteral))
                {
                    continue;
                }

                listing.Versions.Add(versionLiteral, new()
                {
                    Name = qa.PackageId,
                    Version = versionLiteral,
                    Description = qa.Description ?? qa.Title ?? qa.RepositoryUrl,
                    FhirVersion = qa.FhirVersion,
                    Url = qaUrl,
                    Dist = new()
                    {
                        Tarball = qaUrl,
                    },
                });
            }

            if ((listing?.DistTags == null) && (listing?.Versions?.Count > 0))
            {
                listing.DistTags ??= new();

                foreach (FhirCiQaRecord qa in qaRecs.OrderBy(qa => qa.BuildDateIso ?? qa.BuildDate))
                {
                    string? branchName = getBranchFromRepo(qa.RepositoryUrl);
                    string tag = branchName switch
                    {
                        null => "current",
                        "master" => "current",
                        "main" => "current",
                        _ => "current$" + branchName,
                    };

                    if (listing.DistTags.ContainsKey(tag))
                    {
                        continue;
                    }

                    string versionLiteral = buildVersionString(qa);
                    listing.DistTags.Add(tag, versionLiteral);
                }
            }

            return listing;
        }

        /// <summary>
        /// Download the package listing.
        /// </summary>
        /// <param name="pkgname">Name of the package.</param>
        /// <returns>Package listing.</returns>
        public async ValueTask<PackageListing?> DownloadListingAsync(string pkgname)
        {
            (FhirCiQaRecord[] _, Dictionary<string, List<FhirCiQaRecord>> qasByPackageId) = await getQAs();

            if (string.IsNullOrEmpty(pkgname) ||
                !qasByPackageId.TryGetValue(pkgname, out List<FhirCiQaRecord>? qaRecs) ||
                qaRecs.Count == 0)
            {
                return null;
            }

            return listingFromQaRecs(qaRecs);
        }

        private static string? getBranchFromRepo(string? partialRepoLiteral)
        {
            if ((partialRepoLiteral == null) ||
                string.IsNullOrEmpty(partialRepoLiteral) ||
                partialRepoLiteral.Contains("branches/main") ||
                partialRepoLiteral.Contains("branches/master") ||
                partialRepoLiteral.Contains("tree/main") ||
                partialRepoLiteral.Contains("tree/main"))
            {
                return null;
            }

            int branchStart = partialRepoLiteral.IndexOf("branches/") + 9;

            if (branchStart == -1)
            {
                branchStart = partialRepoLiteral.IndexOf("tree/") + 5;
            }

            if (branchStart == -1)
            {
                return null;
            }

            int branchEnd = partialRepoLiteral.IndexOf('/', branchStart);

            return branchEnd == -1 ? partialRepoLiteral.Substring(branchStart) : partialRepoLiteral.Substring(branchStart, branchEnd - branchStart);
        }

        /// <summary>Get a list of package catalogs, based on optional parameters.</summary>
        /// <param name="pkgname">    (Optional) Name of the package.</param>
        /// <param name="fhirversion">(Optional) The FHIR version of a package.</param>
        /// <param name="site">       (Optional) URL of the site.</param>
        /// <param name="repo">       (Optional) The repository.</param>
        /// <param name="branch">     (Optional) The branch.</param>
        /// <param name="preview">    (Optional) Allow for prelease packages.</param>
        /// <returns>A list of package catalogs that conform to the parameters.</returns>
        public async ValueTask<List<PackageCatalogEntry>> CatalogPackagesAsync(
            string? pkgname = null,
            string? fhirversion = null,
            string? site = null,
            string? repo = null,
            string? branch = null,
            bool preview = true)
        {
            List<PackageCatalogEntry> entries = new();

            (FhirCiQaRecord[] qas, Dictionary<string, List<FhirCiQaRecord>> qasByPackageId) = await getQAs();

            HashSet<string> usedIds = new();

            // remove any trailing slashes from the site URL - QAs.json does not have them
            if ((site != null) && site.EndsWith("/"))
            {
                site = site.Substring(0, site.Length - 1);
            }

            // sanitize any repository URL - QAs.json does not repeat the GitHub URL portion
            if ((repo != null) && repo.StartsWith("http://github.com/"))
            {
                repo = repo.Substring(18);
            }
            else if ((repo != null) && repo.StartsWith("https://github.com/"))
            {
                repo = repo.Substring(19);
            }

            if ((branch != null) && (!branch.Contains('/')))
            {
                branch = "/branches/" + branch + "/qa.json";
            }

            // if there was a package name provided, we can use our dictionary for lookup
            if (pkgname != null)
            {
                if (!qasByPackageId.TryGetValue(pkgname, out List<FhirCiQaRecord>? qasRecs))
                {
                    return entries;
                }
                
                // iterate over the records in our dictionary
                foreach (FhirCiQaRecord qa in qasRecs)
                {
                    // skip anything we have already added - duplicates are likely forks but we lose that granularity here
                    if (usedIds.Contains(qa.PackageId ?? string.Empty))
                    {
                        continue;
                    }

                    if ((fhirversion != null) && (qa.FhirVersion != fhirversion))
                    {
                        continue;
                    }

                    if ((site != null) && (qa.Url != site))
                    {
                        continue;
                    }

                    if ((repo != null) && (!qa.RepositoryUrl?.StartsWith(repo) ?? false))
                    {
                        continue;
                    }

                    if ((branch != null) && (!qa.RepositoryUrl?.EndsWith(branch) ?? false))
                    {
                        continue;
                    }

                    // only use this package if it passes all the other filters
                    usedIds.Add(qa.PackageId ?? string.Empty);
                    entries.Add(new PackageCatalogEntry()
                    {
                        Name = qa.PackageId,
                        Description = qa.RepositoryUrl,
                        FhirVersion = qa.FhirVersion,
                    });
                }

                return entries;
            }

            // iterate over all the QA records
            foreach (FhirCiQaRecord qa in qas)
            {
                // skip anything we have already added - duplicates are likely forks but we lose that granularity here
                if (usedIds.Contains(qa.PackageId ?? string.Empty))
                {
                    continue;
                }

                if ((fhirversion != null) && (qa.FhirVersion != fhirversion))
                {
                    continue;
                }

                if ((site != null) && (qa.Url != site))
                {
                    continue;
                }

                if ((repo != null) && (!qa.RepositoryUrl?.StartsWith(repo) ?? false))
                {
                    continue;
                }

                if ((branch != null) && (!qa.RepositoryUrl?.EndsWith(branch) ?? false))
                {
                    continue;
                }

                // only use this package if it passes all the other filters
                usedIds.Add(qa.PackageId ?? string.Empty);
                entries.Add(new PackageCatalogEntry()
                {
                    Name = qa.PackageId,
                    Description = qa.RepositoryUrl,
                    FhirVersion = qa.FhirVersion,
                });
            }

            return entries;
        }

        private static string cleanForSemVer(string value)
        {
            List<char> clean = new(value.Length);

            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    clean.Add(c);
                    continue;
                }

                clean.Add('-');
            }

            return new string(clean.ToArray());
        }

        private FhirCiQaRecord? qaRecordFromPackageRef(PackageReference pr)
        {
            (FhirCiQaRecord[] _, Dictionary<string, List<FhirCiQaRecord>> qasByPackageId) = getQAs().Result;

            if (string.IsNullOrEmpty(pr.Name) ||
                (!qasByPackageId.TryGetValue(pr.Name!, out List<FhirCiQaRecord>? qaRecs)))
            {
                return null;
            }

            // no version resolves to 'current' by default
            string requestedVersion = pr.Version ?? "current";

            // check for using a tag and resolve it to a version
            if (!requestedVersion.Contains('+'))
            {
                PackageListing? listing = listingFromQaRecs(qaRecs);

                if ((listing != null) &&
                    (listing.DistTags?.TryGetValue(requestedVersion, out string? version) == true) &&
                    (listing.Versions?.TryGetValue(version, out PackageRelease? _) == true))
                {
                    requestedVersion = version;
                }
            }

            string[] rvComponents = requestedVersion.Split('+');

            if ((rvComponents.Length > 1) &&
                DateTimeOffset.TryParseExact(
                    rvComponents.Last(), 
                    _ciVersionDateFormat, 
                    CultureInfo.InvariantCulture.DateTimeFormat, 
                    DateTimeStyles.None, out DateTimeOffset requestedDto))
            {
                // traverse the records in the package looking for a match
                foreach (FhirCiQaRecord qa in qaRecs)
                {
                    if ((qa.BuildDateIso == requestedDto) ||
                        (qa.BuildDate == requestedDto))
                    {
                        return qa;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Downloads a package from the source.
        /// </summary>
        /// <param name="reference">Package reference of the package to be downloaded.</param>
        /// <returns>Package content as a byte array.</returns>
        private async ValueTask<byte[]> downloadPackage(PackageReference reference)
        {
            if (reference.Scope != FhirCiScope)
            {
                throw new Exception($"CI packages must be tagged with the Scope: {FhirCiScope}!");
            }

            // find our package in the QA listings
            FhirCiQaRecord? qa = qaRecordFromPackageRef(reference);

            if (qa == null)
            {
                throw new Exception($"Cannot resolve {reference.Moniker} in CI Build QA records!");
            }

            // extract the branch name
            string? branchName = getBranchFromRepo(qa.RepositoryUrl);

            // build the URL
            int igUrlIndex = qa.Url?.IndexOf("/ImplementationGuide/", StringComparison.Ordinal) ?? -1;
            string url = igUrlIndex == -1 ? qa.Url! : qa.Url!.Substring(0, igUrlIndex);

            // if we have a branch name, insert it into the URL
            url += branchName switch
            {
                null => "/package.tgz",
                _ => "/branches/" + branchName + "/package.tgz",
            };

            // download data
            return await _httpClient.GetByteArrayAsync(url).ConfigureAwait(false);
        }

        public async ValueTask<PackageReference> GetCurrent(string name, string? branchName = null)
        {
            PackageListing? listing = await DownloadListingAsync(name);
            if ((listing == null) ||
                (listing.DistTags == null) ||
                (listing.DistTags.Count == 0))
            {
                return PackageReference.None;
            }

            string tag = branchName switch
            {
                null => "current",
                "master" => "current",
                "main" => "current",
                _ => "current$" + branchName,
            };

            if (listing.DistTags.TryGetValue(tag, out string? versionLiteral))
            {
                return new PackageReference(FhirCiScope, listing.Id ?? name, versionLiteral);
            }

            return PackageReference.None;
        }


        /// <summary>
        /// Retrieve the different versions of a package.
        /// </summary>
        /// <param name="name">Package name.</param>
        /// <returns>List of versions.</returns>
        public async Task<Versions?> GetVersions(string name)
        {
            PackageListing? listing = await DownloadListingAsync(name);
            if (listing == null)
            {
                return new();
            }

            Versions v = listing.ToVersions();

            if (v.Items.Count != listing.Versions!.Count)
            {
                throw new Exception("Version count mismatch!");
            }

            return v;

            //return listing is null ? new Versions() : listing.ToVersions();
        }

        /// <summary>
        /// Download a package from the source.
        /// </summary>
        /// <param name="reference">Package reference of the package to be downloaded.</param>
        /// <returns>Package content as byte array.</returns>
        public async Task<byte[]> GetPackage(PackageReference reference)
        {
            return await downloadPackage(reference);
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