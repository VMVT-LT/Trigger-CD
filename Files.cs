
namespace App;


public static class Files {

	public static void CleanDir(string dir, bool recursive, Log log, List<string>? except = null, string? root = null) {
		try {
			root ??= dir;
			if (Directory.Exists(dir)) {
				foreach (string file in Directory.GetFiles(dir))
					if (except is null || !except.Contains(GetRelative(file, root)))
						File.Delete(file);
				if (except is null || !except.Contains(GetRelative(dir, root)))
					foreach (string subDirectory in Directory.GetDirectories(dir))
						CleanDir(subDirectory, recursive, log, except, root);

				if (root is null) {
					try { Directory.Delete(dir, false); } catch (Exception) { } //not recursive
				}
			}
		}
		catch (Exception ex) { log.Print("Error", new { Error = "CleanDir", ex.Message, ex.StackTrace }); throw; }
	}

	public static string GetRelative(string path, string dir) {
		try {
			return path[((Path.GetDirectoryName(dir + "//") ?? "").Length + 1)..];
		}
		catch (Exception) { return ""; }
	}

	private static readonly string IdPath = "data/id.incr";
	private static long _currentId = -1;

	public static long GetNextId() {
		if (_currentId == -1) {
			if (File.Exists(IdPath))
				_ = long.TryParse(File.ReadAllText(IdPath), out _currentId);
			else _currentId = 0;
		}
		_currentId++;
		File.WriteAllText(IdPath, _currentId.ToString());
		return _currentId;
	}
}

