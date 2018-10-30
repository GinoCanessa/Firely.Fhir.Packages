﻿namespace Hl7.Fhir.Packages
{ 
    public class NodePackageUrlProvider : IPackageUrlProvider
    {
        public string Root { get; private set; } 

        public NodePackageUrlProvider(string root = null)
        {
            this.Root = root?.TrimEnd('/') ?? "https://registry.npmjs.org";
        }

        public string GetPackageListingUrl(PackageReference reference) =>
            (reference.Scope == null) 
            ? $"{Root}/{reference.Name}"    
            : $"{Root}/@{reference.Scope}%2F{reference.Name}";

        public string GetPackageUrl(PackageReference reference)
        {
            return
                (reference.Scope == null)
                ? $"{Root}/{reference.Name}/-/{reference.Name}-{reference.Version}.tgz"
                : $"{Root}/@{reference.Scope}/{reference.Name}/-/{reference.Name}-{reference.Version}.tgz";
        }

        public override string ToString() => $"(NPM) {Root}";
        
    }

    public class FhirPackageUrlProvider : IPackageUrlProvider
    {
        public string Root { get; private set; }

        public FhirPackageUrlProvider(string root)
        {
            this.Root = root.TrimEnd('/');
        }

        public string GetPackageListingUrl(PackageReference reference) => $"{Root}/{reference.Name}";

        public string GetPackageUrl(PackageReference reference) => $"{Root}/{reference.Name}/{reference.Version}";

        public override string ToString() => $"(FHIR) {Root}";
    }

    public static class PackageUrlProvider
    {
        public static IPackageUrlProvider Npm => new NodePackageUrlProvider("https://registry.npmjs.org");
        public static IPackageUrlProvider Simplifier => new FhirPackageUrlProvider("https://packages.simplifier.net");
        public static IPackageUrlProvider SimplifierNpm => new NodePackageUrlProvider("https://packages.simplifier.net");
        public static IPackageUrlProvider Staging => new NodePackageUrlProvider("https://packages-staging.simplifier.net");
        public static IPackageUrlProvider Localhost => new NodePackageUrlProvider("https://packages.localhost:3000/");

    }



}
