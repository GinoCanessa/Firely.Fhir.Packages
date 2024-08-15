﻿/* 
 * Copyright (c) 2022, Firely (info@fire.ly) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://github.com/FirelyTeam/Firely.Fhir.Packages/blob/master/LICENSE
 */


#nullable enable

using SemanticVersioning;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Firely.Fhir.Packages
{

    public static class VersionsExtensions
    {
        /// <summary>
        /// Gets the regular expression for matching known core package names.
        /// </summary>
        /// <returns>A regular expression.</returns>
        private static readonly Regex _matchCorePackageOnly = new Regex("^hl7\\.fhir\\.(r\\d+[A-Za-z]?)\\.core$", RegexOptions.Compiled);

        /// <summary>
        /// Retrieves the FHIR versions from a dictionary of package ids and versions.
        /// </summary>
        /// <param name="packages">The dictionary of package ids and versions.</param>
        /// <returns>A list of FHIR version numbers if provided (e.g., 4.0.1), R-literals if not (e.g., R4).</returns>
        public static List<string> FhirVersionsFromPackages(Dictionary<string, string?>? packages)
        {
            List<string> fhirVersions = new();

            if (packages == null)
            {
                return fhirVersions;
            }

            foreach ((string packageId, string? version) in packages)
            {
                Match match = _matchCorePackageOnly.Match(packageId);
                if (!match.Success)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(version))
                {
                    fhirVersions.Add(match.Groups[0].Value.ToUpperInvariant());
                }
                else
                {
                    fhirVersions.Add(version!);
                }
            }

            return fhirVersions;
        }

        /// <summary>
        /// Retrieves the FHIR versions from a collection of package ids.
        /// </summary>
        /// <param name="packageIds">The collection of package ids.</param>
        /// <returns>A list of FHIR version R-literals (e.g., R4).</returns>
        public static List<string> FhirVersionsFromPackages(IEnumerable<string>? packageIds)
        {
            List<string> fhirVersions = new();

            if (packageIds == null)
            {
                return fhirVersions;
            }

            foreach (string packageId in packageIds)
            {
                Match match = _matchCorePackageOnly.Match(packageId);
                if (!match.Success)
                {
                    continue;
                }

                fhirVersions.Add(match.Groups[0].Value.ToUpperInvariant());
            }

            return fhirVersions;
        }

        /// <summary>
        /// Returns version information based on this listing
        /// </summary>
        /// <param name="listing"></param>
        /// <returns>A list of package versions</returns>
        public static Versions ToVersions(this PackageListing listing)
        {
            var listed = listing.GetListedVersionStrings();
            var unlisted = listing.GetUnlistedVersionStrings();
            return new Versions(listed, unlisted);
        }

        //"unlisted" is defined by us, it's not part of npm. npm has a deprecated warning. The "unlisted" field is currently a string, but we expect to transform it to a boolean "true" / "false".
        internal static IEnumerable<string> GetUnlistedVersionStrings(this PackageListing listing)
        {
            return listing.Versions?.Where(v => !string.IsNullOrEmpty(v.Value.Unlisted)).Select(v => v.Key) ?? new List<string> { };
        }

        //"unlisted" is defined by us, it's not part of npm. npm has a deprecated warning. The "unlisted" field is currently a string, but we expect to transform it to a boolean "true" / "false".
        internal static IEnumerable<string> GetListedVersionStrings(this PackageListing listing)
        {
            return listing.Versions?.Where(v => string.IsNullOrEmpty(v.Value.Unlisted)).Select(v => v.Key) ?? new List<string> { };
        }

        /// <summary>
        /// Returns version information based on this list of package references
        /// </summary>
        /// <param name="references"></param>
        /// <returns>A list of package versions</returns>
        public static Versions ToVersions(this IEnumerable<PackageReference> references)
        {
            var versions = references.Select(r => r.Version).Where(v => v is not null);

            return versions is null || !versions.Any() ? new Versions() : new Versions(versions!);
        }

        internal static Version? Resolve(this Versions versions, string? pattern)
        {
            if (pattern == null)
                return null;

            if (pattern == "latest" || string.IsNullOrEmpty(pattern))
            {
                return versions.Latest();
            }
            var range = new Range(pattern);
            return versions.Resolve(range);
        }

        /// <summary>
        /// Resolve a package dependency
        /// </summary>
        /// <param name="versions"></param>
        /// <param name="dependency">The package reference to be resolves</param>
        /// <param name="stable">Indication of allowing only non-preview versions</param>
        /// <returns>A package reference describing the dependency</returns>
        public static PackageReference Resolve(this Versions versions, PackageDependency dependency, bool stable = false)
        {
            var version = (dependency.Range is null) ? null : versions.Resolve(dependency.Range, stable);
            return version is null ? PackageReference.None : new PackageReference(dependency.Name, version.ToString());
        }

        /// <summary>
        /// A boolean checking if this <see cref="Versions"/> object contains a specific version 
        /// </summary>
        /// <param name="versions"></param>
        /// <param name="version">Specific version to be checked</param>
        /// <returns>Whether of not a specific version is present in the <see cref="Versions"/> object </returns>
        public static bool Has(this Versions versions, string version)
        {
            return Version.TryParse(version, out var v) && versions.Has(v);
        }
    }
}

#nullable restore
