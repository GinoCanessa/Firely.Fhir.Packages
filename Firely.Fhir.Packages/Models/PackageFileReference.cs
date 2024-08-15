/* 
 * Copyright (c) 2022, Firely (info@fire.ly) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://github.com/FirelyTeam/Firely.Fhir.Packages/blob/master/LICENSE
 */


#nullable enable

using System.IO;

namespace Firely.Fhir.Packages
{
    /// <summary>
    /// A reference to a file in a package, or pseudo package.
    /// </summary>
    public class PackageFileReference : ResourceMetadata
    {
        /// <summary>
        /// Initialized a new <see cref="PackageFileReference"/>
        /// </summary>
        /// <param name="filename">Name of the file</param>
        /// <param name="filePath">File path, relative to the package root</param>
        public PackageFileReference(string filename, string filePath) : base(filename, filePath)
        {
            FileName = filename;
            FilePath = filePath;
        }

        /// <summary>
        /// Initialized a new <see cref="PackageFileReference"/>
        /// </summary>
        /// <param name="filePath">File path, relative to the package root</param>
        public PackageFileReference(string filePath) : base(filePath)
        {
            FileName = Path.GetFileName(filePath);
            FilePath = filePath;
        }

        /// <summary>
        /// references to the package that this file belongs to
        /// </summary>
        public PackageReference Package;
    }

}

#nullable restore