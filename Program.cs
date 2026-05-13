using App;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets<Program>(true);
var app = builder.Build();

var cfg = new Config(app);

if (!Directory.Exists("data")) { Directory.CreateDirectory("data"); }
if (!Directory.Exists("data/files")) { Directory.CreateDirectory("data/files"); }

app.MapGet("/{app}/{key?}", async (HttpContext ctx, string app, string? key, string? force=null) => {
	if(cfg.Apps.TryGetValue(app.ToLower(), out var cfgApp) && cfgApp is not null) {
		if(cfgApp.Key==key && cfgApp.Path is not null) {
			var rp = cfgApp.Repo;
			if(rp is not null) {
				var now = DateTime.UtcNow;
				if (cfgApp.Running > DateTime.UtcNow) await ctx.Response.WriteAsync("Running");
				else if (cfgApp.Lock > DateTime.UtcNow) await ctx.Response.WriteAsync("Locked");
				else {
					cfgApp.Running = now.AddSeconds(cfg.Lock + cfg.WaitTime + cfg.Delay + 30);
					cfgApp.Lock = now.AddSeconds(cfg.Lock);
					_ = Task.Run(async () => {
						var log = new Log(cfgApp.Name!);
						log.Print("Trigger", "Start");
						var debug = cfgApp.Debug ?? false;
						await Task.Delay(cfg.Delay * 1000);

						if (await rp.Download(cfgApp.Path, log, (force == "" || force?.ToLower() == "true"), cfg.WaitTime, debug)) {

							try {
								if (!string.IsNullOrEmpty(cfgApp.Service)) {
									if (debug) log.Print("Debug", $"Stopping service {cfgApp.Service} ({$"systemctl start {cfgApp.Service}"})");
									Extensions.ShellExec("/usr/bin/sudo", $"systemctl stop {cfgApp.Service}", log);
								}

								if (cfgApp.Clean??false) {
									if (debug) log.Print("Debug", $"Cleaning directory");
									Files.CleanDir(cfgApp.Path, true, log, cfgApp.SkipClean);
								}

								if (debug) log.Print("Debug", $"Extracting files");
								ZipFile.ExtractToDirectory($"data/files/{cfgApp.Name}.zip", cfgApp.Path, true);

								if (debug) log.Print("Debug", $"Permission setup");
								if (debug) log.Print("Debug", $"# /usr/bin/chown -R {cfgApp.ChOwn} {cfgApp.Path}");
								Extensions.ShellExec("/usr/bin/chown", $"-R {cfgApp.ChOwn} {cfgApp.Path}", log);
								if (debug) log.Print("Debug", $"# /usr/bin/chmod -R {cfgApp.ChMod} {cfgApp.Path}");
								Extensions.ShellExec("/usr/bin/chmod", $"-R {cfgApp.ChMod} {cfgApp.Path}", log);

								if (!string.IsNullOrEmpty(cfgApp.Script)) {
									if (debug) log.Print("Debug", $"Running script ({cfgApp.Script})");
									Extensions.ShellExec("/bin/bash", $"-c \"{cfgApp.Script.Replace("\"", "\\\"")}\"", log);
								}

								if (!string.IsNullOrEmpty(cfgApp.Service)) {
									if (debug) log.Print("Debug", $"Starting service {cfgApp.Service} ({$"systemctl start {cfgApp.Service}"})");
									Extensions.ShellExec("/usr/bin/sudo", $"systemctl start {cfgApp.Service}", log);
								}
							}
							catch (Exception ex) { log.Print("Error", new { Error = "General", ex.Message, ex.StackTrace }); }
						}
						log.Print("Trigger", "Done");
						cfgApp.Running = now;
					});
					await ctx.Response.WriteAsync("Ok");
				}
			}
		}
		else ctx.Response.StatusCode = 401;
	} 
	else ctx.Response.StatusCode = 404;
});

app.Run();
