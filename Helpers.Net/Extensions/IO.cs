using Helpers.Net.Utilities;
using System;
using System.Collections.Generic;
using System.IO;

namespace Helpers.Net.Extensions
{
	public static class IO
	{
		public static bool TrySyncDirectory(string sourceFolder, string destFolder, bool deleteExtra = false,
			bool recursive = false)
		{
			if (!TryCreateDirectory(destFolder))
				return false;

			foreach (var file in Directory.EnumerateFiles(sourceFolder, "*.*"))
			{
				var destFile = Path.Combine(destFolder, Path.GetFileName(file));

				var copyRequired = true;
				if (File.Exists(destFile))
				{
					var sourceInfo = new FileInfo(file);
					var destInfo = new FileInfo(destFile);

					copyRequired = sourceInfo.LastWriteTime != destInfo.LastWriteTime ||
								   sourceInfo.Length != destInfo.Length;
				}

				if (copyRequired)
				{
					if (!TryCopyFile(file, destFile))
						return false;
				}
			}

			if (deleteExtra)
			{
				foreach (var file in Directory.EnumerateFiles(destFolder, "*.*"))
				{
					var sourceFile = Path.Combine(sourceFolder, Path.GetFileName(file));
					if (!File.Exists(sourceFile))
						if (!TryDeleteFile(file)) return false;
				}
			}

			if (recursive)
			{
				foreach (var folder in Directory.EnumerateDirectories(sourceFolder))
				{
					var dest = Path.Combine(destFolder, Path.GetFileName(folder));

					if (!TrySyncDirectory(folder, dest, deleteExtra, recursive))
						return false;
				}

				if (deleteExtra)
				{
					foreach (var folder in Directory.EnumerateDirectories(destFolder))
					{
						var source = Path.Combine(sourceFolder, Path.GetFileName(folder));

						if (!Directory.Exists(source))
							if (!TryDeleteDirectory(folder, true)) return false;
					}
				}
			}

			return true;
		}

		public static bool TryCreateDirectory(string path, bool createParent = true)
		{
			try
			{
				if (Directory.Exists(path))
					return true;

				var di = new DirectoryInfo(path);
				if (di.Parent != null && !di.Parent.Exists)
				{
					if (!createParent || !TryCreateDirectory(di.Parent.FullName, true))
						return false;
				}

				if (!Directory.Exists(path))
					Directory.CreateDirectory(path);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public static bool TryDeleteDirectory(string path, bool recursive = false)
		{
			try
			{

				if (!Directory.Exists(path))
					return true;

				if (recursive)
				{
					foreach (var subdir in Directory.EnumerateDirectories(path))
					{
						if (!TryDeleteDirectory(subdir, recursive)) return false;
					}

					foreach (var file in Directory.EnumerateFiles(path))
					{
						if (!TryDeleteFile(file)) return false;
					}
				}

				Directory.Delete(path, recursive);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public static bool TryDeleteFiles(IEnumerable<string> files, Progress progress = null)
		{
			var result = true;
			foreach (var file in files)
			{
				if (!TryDeleteFile(file))
				{
					result = false;
				}

				if (progress != null) progress++;
			}
			return result;
		}

		public static bool TryRenameFile(string sourceFile, string destFile, bool deleteExisting = true)
		{
			try
			{
				if (!File.Exists(sourceFile)) return false;

				if (new FileInfo(sourceFile).IsReadOnly)
					File.SetAttributes(sourceFile, FileAttributes.Normal);

				if (File.Exists(destFile))
				{
					if (!deleteExisting) return false;

					if (new FileInfo(destFile).IsReadOnly)
						File.SetAttributes(destFile, FileAttributes.Normal);

					File.Delete(destFile);
				}

				File.Move(sourceFile, destFile);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public static bool TryDeleteFile(string file)
		{
			try
			{
				if (!File.Exists(file))
					return true;

				if (new FileInfo(file).IsReadOnly)
					File.SetAttributes(file, FileAttributes.Normal);

				File.Delete(file);
				return !File.Exists(file);
			}
			catch (Exception)
			{
				return false;
			}
		}

		public static
			bool TryCopyFile(string sourceFile, string destFile, bool copyOverExisting = true,
				bool createDirectory = true, bool useStreamCopy = false)
		{
			try
			{
				if (sourceFile == destFile)
					return true;

				var destFolder = Path.GetDirectoryName(destFile) ?? "";
				if (!Directory.Exists(destFolder))
				{
					if (createDirectory)
					{
						if (!TryCreateDirectory(destFolder))
							return false;
					}
					else
					{
						return false;
					}
				}
				if (useStreamCopy)
				{

					using (var input = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.None))
					{
						using (
							var output = new FileStream(destFile,
								copyOverExisting ? FileMode.Create : FileMode.CreateNew, FileAccess.Write,
								FileShare.None))
						{
							input.CopyTo(output);
						}
					}
				}
				else
				{
					
					try
					{
						File.Copy(sourceFile, destFile, copyOverExisting);
					}
					catch (Exception)
					{
						if (copyOverExisting && File.Exists(destFile) && new FileInfo(destFile).IsReadOnly)
						{
							File.SetAttributes(destFile, FileAttributes.Normal);
							File.Copy(sourceFile, destFile, copyOverExisting);
						}
						else throw;
					}
					
				}
				
				return File.Exists(destFile);
			}
			catch (Exception)
			{
				return false;
			}
		}

		public static bool TryMoveFile(string sourceFile, string destFile, bool copyOverExisting = true, bool createDirectory = true)
		{
			try
			{
				if (sourceFile == destFile)
					return true;

				var destFolder = Path.GetDirectoryName(destFile) ?? "";
				if (!Directory.Exists(destFolder))
				{
					if (createDirectory)
					{
						if (!TryCreateDirectory(destFolder))
							return false;
					}
					else
					{
						return false;
					}
				}

				File.Copy(sourceFile, destFile, copyOverExisting);
				if (File.Exists(destFile))
				{
					try
					{
						File.Delete(sourceFile);
					}
					catch (Exception)
					{
					}

					return true;
				}

				return false;
			}
			catch (Exception)
			{
				return false;
			}	
		}

		public static bool TryReadFile(string filename)
		{
			if (!File.Exists(filename))
				return false;

			try
			{
				// Sometimes files that have been archived need to be retrieved from storage. Reading from the file
				// is needed before trying to copy the file. 
				using (var reader = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				{
					reader.ReadByte();
				}
			}
			catch
			{
				return false;
			}

			return true;
		}

		public static bool TryReadAll(List<string> files)
		{
			return files.TrueForAll(TryReadFile);
		}
	}
}
