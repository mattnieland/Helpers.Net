using System.IO;

namespace Helpers.Net.Extensions
{
    /// <summary>
    /// 	Extension methods for the FileInfo and FileInfo-Array classes
    /// </summary>
    public static class FileInfoExtensions
    {                              
        /// <summary>
        /// 	Sets file attributes for several files at once
        /// </summary>
        /// <param name = "files">The files.</param>
        /// <param name = "attributes">The attributes to be set.</param>
        /// <returns>The changed files</returns>
        /// <example>
        /// 	<code>
        /// 		var files = directory.GetFiles("*.txt", "*.xml");
        /// 		files.SetAttributes(FileAttributes.Archive);
        /// 	</code>
        /// </example>
        public static FileInfo[] SetAttributes(this FileInfo[] files, FileAttributes attributes)
        {
            foreach (var file in files)
                file.Attributes = attributes;
            return files;
        }

        /// <summary>
        /// 	Appends file attributes for several files at once (additive to any existing attributes)
        /// </summary>
        /// <param name = "files">The files.</param>
        /// <param name = "attributes">The attributes to be set.</param>
        /// <returns>The changed files</returns>
        /// <example>
        /// 	<code>
        /// 		var files = directory.GetFiles("*.txt", "*.xml");
        /// 		files.SetAttributesAdditive(FileAttributes.Archive);
        /// 	</code>
        /// </example>
        public static FileInfo[] SetAttributesAdditive(this FileInfo[] files, FileAttributes attributes)
        {
            foreach (var file in files)
                file.Attributes = (file.Attributes | attributes);
            return files;
        }
    }
}
