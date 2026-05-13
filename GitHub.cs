using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace App;

public class GitHub {
	private static HttpClient HClient { get; }
	public string? App { get; set; }
	public string? Org { get; set; }
	public string? Repo { get; set; }
	public string? Name { get; set; }
	public string? Token { get; set; }

	static GitHub() {
		HClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = true });
		HClient.DefaultRequestHeaders.Add("User-Agent", "A1-CD/0.2");
	}


	public async Task<bool> Download(string path, Log log, bool force = false, int retry = 0, bool debug=true) {
		if (Repo is not null && Name is not null && App is not null) {
			using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Org}/{Repo}/actions/artifacts?name={Name}&per_page=1");
			if (Token is not null) req.Headers.Authorization = new("token", Token);

			try {
				if (debug) log.Print("Debug",$"Git - Url https://api.github.com/repos/{Org}/{Repo}/actions/artifacts?name={Name}&per_page=1");
				using var response = await HClient.SendAsync(req);
				if (debug) log.Print("Debug", $"Git - Status {response.StatusCode}");
				if (response.IsSuccessStatusCode) {
					var dta = await response.Content.ReadFromJsonAsync<GHArtifacts>();
					if (debug) log.Print("Debug", $"Git - Artifacts {JsonSerializer.Serialize(dta)}");
					var af = dta?.Artifacts?.FirstOrDefault();
					if (af is not null) {
						var vrs = Version.Get(App);
						vrs.Log ??= [];
						if (force || vrs.Id < af.Id) {
							if (debug) log.Print("Debug", $"Git - Download {af.Download}");
							using var dlr = new HttpRequestMessage(HttpMethod.Get, af.Download);
							if (Token is not null) dlr.Headers.Authorization = new("token", Token);
							using var flr = await HClient.SendAsync(dlr);

							if (debug) log.Print("Debug", $"Git - Download Status {flr.StatusCode}");
							if (flr.IsSuccessStatusCode) {
								try {
									var pth = $"data/files/{App}.zip";
									using (var dls = new FileStream(pth, FileMode.Create)) await flr.Content.CopyToAsync(dls);

									log.Print("Release", $"{af.Id} ({af.Size_in_bytes / 1024:0.##}KB){(force ? " (force)" : "")}");
									vrs.Id = af.Id; vrs.Date = af.Created_at; vrs.Url = af.Download;
									vrs.Log.Add($"{af.Created_at:yyyy-MM-ddTHH:mm:ssZ}|{af.Id}|{af.Size_in_bytes}");
									vrs.Save();
									return true;

								} catch (Exception ex) {
									log.Print("GitError", new { Error = "Archive", ex.Message, ex.StackTrace });
								}

							}
							else {
								var rsp = "";
								try { rsp = await flr.Content.ReadAsStringAsync(); } catch (Exception) { }
								log.Print("GitError", new { Error = "Download", Status = response.StatusCode, Code = (int)response.StatusCode, Response = rsp });
							}
						}
						else {
							if (retry > 0) {
								log.Print("Git", $"Waiting ({retry})");
								Thread.Sleep(1000); retry--;
								return await Download(path, log, force, retry, debug);
							}
							else {
								log.Print("Git", $"No new artifacts ({vrs.Id})");
								vrs.Log.Add($"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}|{af.Id}|Skip");
								vrs.Save();
								return false;
							}
						}
					}
					else log.Print("GitError", new { Error = "No artifacts", Response = JsonSerializer.Serialize(dta) });
				}
				else {
					var rsp = "";
					try { rsp = await response.Content.ReadAsStringAsync(); } catch (Exception) { }
					log.Print("GitError", new { Error = "Request", Status = response.StatusCode, Code = (int)response.StatusCode, Response = rsp });
				}
			} catch (Exception ex) {
				log.Print("GitError", new { Error = "General", ex.Message, ex.StackTrace });
			}
		}
		else log.Print("GitError", new { Error = "Config", Path = path, Data = JsonSerializer.Serialize(this) });
		return false;
	}

}

public class Version {
	public string? Repo { get; set; }
	public long Id { get; set; }
	public DateTime Date { get; set; }
	public string? Url { get; set; }
	public List<string>? Log { get; set; }

	public static Version Get(string repo) {
		var ret = Extensions.ReadJsonFile<Version>($"data/{repo}.json");
		return ret?.Repo is null ? new(){ Repo=repo, Log=[] } : ret;
	}
	public void Save() => Extensions.SaveJsonFile($"data/{Repo}.json", this);
}



public static class Extensions {
	private static JsonSerializerOptions JsonOpts {get;} = new JsonSerializerOptions() { WriteIndented = true };
	public static string EnsureFile(string file){
		var pth = Path.Combine(Directory.GetCurrentDirectory(), file);
		if (!File.Exists(pth)) { File.WriteAllText(pth, "{}"); }
		return pth;
	}
	public static string ReadFile(string file) => File.ReadAllText(EnsureFile(file));
	public static T? ReadJsonFile<T>(string file) => JsonSerializer.Deserialize<T>(ReadFile(file));
	public static void SaveJsonFile<T>(string file, T obj) {
		var str = JsonSerializer.Serialize(obj, JsonOpts);
		File.WriteAllText(EnsureFile(file), str);
	}

	public static bool ShellExec(string file, string arg, Log log){
		try {
			var prc = new Process(){
				StartInfo= new (){
					FileName = file,
					Arguments = arg,
					UseShellExecute = false,
					RedirectStandardError = true,
				}
			};
			prc.Start();
			string error = prc.StandardError.ReadToEnd();
			prc.WaitForExit();

			if (prc.ExitCode != 0) { log.Print("Error", new { Error = "Execute script", File = file, Args = arg, Message = error }); return false; }
			return true;
		} catch (Exception ex) { log.Print("Error", new { Error="Execute script", File=file, Args=arg, ex.Message, ex.StackTrace }); return false; }
	}
}



public class GHArtifacts {
	[JsonPropertyName("total_count")] public int Count { get; set; }
	public List<GHArtifact>? Artifacts { get; set; }
}


public class GHArtifact {
	public long Id { get; set; }
	public string? Node_Id { get; set; }
	public string? Name { get; set; }
	public long Size_in_bytes { get; set; }
	public string? Url { get; set; }
	[JsonPropertyName("archive_download_url")]public string? Download { get; set; }
	public bool Expired { get; set; }
	public DateTime Created_at { get; set; }
	public DateTime Updated_at { get; set; }
	public DateTime Expires_at { get; set; }
}

