using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.IO;

namespace Helpers.Net.Utilities
{
	public class ZipTool
	{
		public string ExecutablePath;
		public string DefaultExtension;

		public int WaitTimeout = (int)TimeSpan.FromMinutes(90).TotalMilliseconds;

		public virtual void AddFiles(string archivePath, IEnumerable<string> sourceFiles, string workingFolder = null)
		{
		}

		public virtual void AddFolder(string archivePath, string sourceFolder, string workingFolder = null, string[] excludedFolders = null)
		{
		}

		public virtual void UpdateFolder(string archivePath, string sourceFolder, string workingFolder=null)
		{
		}

		public virtual void ExtractFiles(string archivePath, string destinationPath, IEnumerable<string> sourceFiles = null, string workingFolder = null)
		{
		}

		protected virtual int ExecuteShellCommand(params string[] args)
		{
			var quotedArgs = args.Select(x => x).ToList();

			var startInfo = new ProcessStartInfo(ExecutablePath, string.Join(" ", quotedArgs))
			{
				UseShellExecute = false
			};

			var proc = Process.Start(startInfo);
			if (proc == null)
				return -1;

			if (!proc.WaitForExit(WaitTimeout))
				throw new Exception(string.Format("Timeout waiting for zip process to complete: {0} {1}", ExecutablePath,
					string.Join(" ", quotedArgs)));

			return proc.ExitCode;
		}

		private class SevenZipTool : ZipTool
		{
			private void AddFile(string archivePath, string sourceFile, string workingFolder = null)
			{
				if (!File.Exists(sourceFile))
					throw new Exception(string.Format("File not found: {0}", sourceFile));

				var result = ExecuteShellCommand("a",
					archivePath.EndsWith(".zip") ? "-tzip" : "",
					"-y",
					string.Format("\"{0}\"", archivePath),
					string.Format("\"{0}\"", sourceFile),
					workingFolder == null ? "" : string.Format("-w\"{0}\"", workingFolder)
					);

				if (result != 0 || !File.Exists(archivePath))
					throw new Exception(string.Format("Error creating archive of file: {0}", sourceFile));
			}

			public override void AddFiles(string archivePath, IEnumerable<string> sourceFiles, string workingFolder = null)
			{
				var tempFolder = workingFolder ?? Path.GetDirectoryName(archivePath) ?? "";

				var listFile = Path.Combine(tempFolder, Path.GetRandomFileName());
				File.WriteAllLines(listFile, sourceFiles);

				var result = ExecuteShellCommand("a",
					archivePath.EndsWith(".zip") ? "-tzip" : "",
					"-y",
					string.Format("\"{0}\"", archivePath),
					string.Format("@\"{0}\"", listFile),
					workingFolder == null ? "" : string.Format("-w\"{0}\"", workingFolder)
					);

				if (result != 0 || !File.Exists(archivePath))
					throw new Exception(string.Format("Error creating archive of folder: {0}", listFile));

				File.Delete(listFile);
			}

			public override void AddFolder(string archivePath, string sourceFolder, string workingFolder = null, string[] excludedFolders = null)
			{
				if (!Directory.Exists(sourceFolder))
					throw new Exception(string.Format("Directory not found: {0}", sourceFolder));

				var excludedFoldersArg = new StringBuilder();
				if (excludedFolders != null)
				{
					foreach (var excludedFolder in excludedFolders)
					{
						excludedFoldersArg.Append(string.Format("-xr!{0} ", excludedFolder));
					}
				}

				var result = ExecuteShellCommand("a", 
					archivePath.EndsWith(".zip") ? "-tzip" : "",
					"-y",
					string.Format("\"{0}\"", archivePath),
					string.Format("\"{0}\\*\"", sourceFolder),
					workingFolder == null ? "" : string.Format("-w\"{0}\"", workingFolder),
					excludedFoldersArg.ToString().Trim()
					);

				if (result != 0 || !File.Exists(archivePath))
					throw new Exception(string.Format("Error creating archive of folder: {0}", sourceFolder));
			}


			public override void UpdateFolder(string archivePath, string sourceFolder, string workingFolder = null)
			{
				if (!Directory.Exists(sourceFolder))
					throw new Exception(string.Format("Directory not found: {0}", sourceFolder));

				var result = ExecuteShellCommand("u",
					archivePath.EndsWith(".zip") ? "-tzip" : "",
					"-y",
					string.Format("\"{0}\"", archivePath),
					string.Format("\"{0}\\*\"", sourceFolder),
					workingFolder == null ? "" : string.Format("-w\"{0}\"", workingFolder)
					);

				if (result != 0 || !File.Exists(archivePath))
					throw new Exception(string.Format("Error creating archive of folder: {0}", sourceFolder));
			}


			public override void ExtractFiles(string archivePath, string destinationPath, IEnumerable<string> sourceFiles = null, string workingFolder = null)
			{
				if (!File.Exists(archivePath))
					throw new Exception(string.Format("Archive file not found: {0}", archivePath));

				if (!Extensions.IO.TryCreateDirectory(destinationPath))
					throw new Exception(string.Format("Could not create destination folder: {0}", destinationPath));

				int result = 0;

				var files = sourceFiles == null ? new List<string>() : sourceFiles.ToList();

				if (files.Count == 0)
				{
					result = ExecuteShellCommand("x",
						"-y",
						string.Format("\"{0}\"", archivePath),
						string.Format("-o\"{0}\"", destinationPath));
				}
				else
				{
					var tempFolder = workingFolder ?? Path.GetDirectoryName(destinationPath) ?? "";

					var listFile = Path.Combine(tempFolder, Path.GetRandomFileName());
					File.WriteAllLines(listFile, files);

					result = ExecuteShellCommand("x",
						"-y",
						string.Format("\"{0}\"", archivePath),
						string.Format("@\"{0}\"", listFile),
						string.Format("-o\"{0}\"", destinationPath));

					File.Delete(listFile);
				}

				if (result != 0)
					throw new Exception(string.Format("Error extracting archive: {0}", archivePath));
			}

			public override bool IsZipFile(string filename)
			{
				var file = filename.ToLower();
				return file.EndsWith(".7z") || file.EndsWith(".zip");
			}
		}

		public static ZipTool GetTool(string toolPath, string extension)
		{

			return new SevenZipTool
			{
				DefaultExtension =  extension,
				ExecutablePath = Path.Combine(toolPath, "7za.exe")
			};
		}

		public virtual bool IsZipFile(string filename)
		{
			return false;
		}
	}

	
}
