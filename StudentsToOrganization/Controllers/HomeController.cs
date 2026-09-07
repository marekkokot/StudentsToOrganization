using Octokit;
using StudentsToOrganization.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using StudentsToOrganization.Other;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;
using System.Web.Management;

namespace StudentsToOrganization.Controllers
{
    public class HomeController : Controller
    {
        #region CONFIGURATION

        // ====================================================================
        //  COURSE / SECRETS CONFIGURATION  (one binary serves all courses)
        // --------------------------------------------------------------------
        //  Course identity + GitHub OAuth secrets come from an external JSON
        //  file (github_secrets.json) that is NOT in version control.
        //
        //  WHERE THE FILE LIVES (Web.config appSettings key "GithubSecretsPath"):
        //      |DataDirectory|..\..\github_secrets\github_secrets.json
        //    |DataDirectory| = the app's App_Data folder. Same ..\.. convention
        //    as the GithubData.db connection string, so the file sits next to
        //    the shared DB\ folder:
        //      Server: C:\inetpub\github_manage\github_secrets\github_secrets.json
        //      Local:  <project-parent>\github_secrets\github_secrets.json
        //
        //  WHICH COURSE AM I?
        //    - SERVER  (Request.IsLocal == false): taken from the IIS app folder
        //      name (e.g. "pk4-katowice"), used as the key into the JSON.
        //      Server credentials are used.
        //    - LOCALHOST (Request.IsLocal == true): the local project folder is
        //      not a course name, so you MUST set LOCALHOST_DEBUG_COURSE below.
        //      Localhost credentials are used. If a course has no "localhost"
        //      block in the JSON (e.g. JAVA), running it locally throws.
        //
        //  TO RUN LOCALLY AFTER A FRESH CLONE (years from now):
        //    1. git clone
        //    2. Recreate the shared artifacts two levels up from App_Data:
        //         <project-parent>\DB\GithubData.db                     (from server)
        //         <project-parent>\github_secrets\github_secrets.json   (from server)
        //    3. Set LOCALHOST_DEBUG_COURSE below to the course key (e.g. "ppk").
        //    4. Build & run.
        // ====================================================================

        // LOCALHOST ONLY: which course to debug. Ignored on the server.
        // Leave null/empty -> running locally throws (fail-fast, no silent wrong course).
        const string LOCALHOST_DEBUG_COURSE = "ppk";

        class OrgConfig
        {
            public readonly string organization;
            public readonly string clientId;
            public readonly string clientSecret;
            public readonly string CourseName;
            public readonly string courseKey;   // IIS folder / JSON key, e.g. "pk4-katowice" (safe for filenames)
            public readonly string courseEnumName;   // value stored in the DB 'Course' column (from JSON courseEnum)

            private static Newtonsoft.Json.Linq.JObject _cachedRoot;
            private static readonly object _lock = new object();

            private static Newtonsoft.Json.Linq.JObject LoadRoot()
            {
                if (_cachedRoot != null) return _cachedRoot;
                lock (_lock)
                {
                    if (_cachedRoot != null) return _cachedRoot;

                    string raw = System.Configuration.ConfigurationManager.AppSettings["GithubSecretsPath"];
                    if (string.IsNullOrEmpty(raw))
                        throw new Exception("Web.config appSettings is missing 'GithubSecretsPath'.");

                    string dataDir = AppDomain.CurrentDomain.GetData("DataDirectory") as string;
                    if (string.IsNullOrEmpty(dataDir))
                        throw new Exception("DataDirectory is not set; cannot resolve GithubSecretsPath.");

                    string path = System.IO.Path.GetFullPath(
                        raw.Replace("|DataDirectory|", dataDir.TrimEnd('\\') + "\\"));

                    if (!System.IO.File.Exists(path))
                        throw new Exception("github_secrets.json not found at expected location: " + path +
                                            "  (See the runbook comment in HomeController for how to place it.)");

                    _cachedRoot = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
                    
                    // Validate: every course must have a 'courseEnum', and no two courses
                    // may share the same 'courseEnum' value. It is the DB 'Course' key —
                    // a duplicate would make two course apps read/write the same student
                    // rows, mixing students across courses. Fail loudly at load time.
                    var coursesForCheck = (Newtonsoft.Json.Linq.JObject)_cachedRoot["courses"];
                    if (coursesForCheck == null)
                        throw new Exception("github_secrets.json has no 'courses' object.");

                    var seenEnum = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in coursesForCheck.Properties())
                    {
                        var enumVal = (string)((Newtonsoft.Json.Linq.JObject)prop.Value)["courseEnum"];
                        if (string.IsNullOrEmpty(enumVal))
                            throw new Exception("Course '" + prop.Name + "' is missing 'courseEnum' in github_secrets.json.");
                        if (!seenEnum.Add(enumVal))
                            throw new Exception("Duplicate 'courseEnum' value '" + enumVal +
                                "' in github_secrets.json. Each course must have a globally unique 'courseEnum' " +
                                "(it is the DB 'Course' key; duplicates would mix students across courses).");
                    }

                    return _cachedRoot;
                }
            }

            private static string ResolveCourseKey(out bool isLocal)
            {
                var ctx = System.Web.HttpContext.Current;

                // Defensive: course/environment detection requires a live request
                // (Request.IsLocal). If cnf is ever touched outside a request, we
                // must NOT silently fall through to server folder-detection — that
                // could pick a wrong course. Fail loudly instead.
                if (ctx == null || ctx.Request == null)
                    throw new Exception(
                        "OrgConfig accessed without an HTTP request context. Course/secret " +
                        "resolution needs Request.IsLocal and cannot run at app startup or " +
                        "from a background thread. (This should not happen from a controller action.)");


                isLocal = ctx.Request.IsLocal;

                if (isLocal)
                {
                    if (string.IsNullOrEmpty(LOCALHOST_DEBUG_COURSE))
                        throw new Exception(
                            "Running on localhost but LOCALHOST_DEBUG_COURSE is not set. " +
                            "Set it in HomeController to a course key such as \"ppk\". See runbook comment.");
                    return LOCALHOST_DEBUG_COURSE;
                }

                // Server: last path segment of the app's physical folder, e.g. "ppk".
                string appPath = System.Web.HttpRuntime.AppDomainAppPath;
                return new System.IO.DirectoryInfo(appPath).Name;
            }

            public OrgConfig()
            {
                var root = LoadRoot();
                bool isLocal;
                string key = ResolveCourseKey(out isLocal);

                var courses = (Newtonsoft.Json.Linq.JObject)root["courses"];

                Newtonsoft.Json.Linq.JProperty match = null;
                foreach (var prop in courses.Properties())
                    if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase)) { match = prop; break; }

                if (match == null)
                    throw new Exception("Course key '" + key + "' not found in github_secrets.json " +
                        (isLocal ? "(localhost / LOCALHOST_DEBUG_COURSE)." : "(server / app folder name)."));

                var c = (Newtonsoft.Json.Linq.JObject)match.Value;

                organization = (string)c["organization"];
                CourseName = (string)c["courseName"];
                courseKey = match.Name;
                courseEnumName = (string)c["courseEnum"];

                string block = isLocal ? "localhost" : "server";
                var creds = (Newtonsoft.Json.Linq.JObject)c[block];
                if (creds == null)
                    throw new Exception("Course '" + key + "' has no '" + block + "' credentials in github_secrets.json" +
                        (isLocal ? " (this course has no localhost app; cannot run it locally)." : "."));

                clientId = (string)creds["clientId"];
                clientSecret = (string)creds["clientSecret"];
            }
        }

        // 'cnf' is lazy: it needs a request (Request.IsLocal), so it can't be
        // initialized at class-load. Built once on first use, then cached.
        static OrgConfig _cnf;
        static OrgConfig cnf { get { return _cnf ?? (_cnf = new OrgConfig()); } }

        readonly string organization = cnf.organization;
        readonly string clientId = cnf.clientId;
        readonly string clientSecret = cnf.clientSecret;

        const int expcetion_retries = 15;//octocit quite often throws exceptions...

        #endregion

        readonly GitHubClient client = new GitHubClient(new ProductHeaderValue("StudentsToOrganization"));

        [ChildActionOnly]
        public ActionResult CourseName()
        {
            return PartialView("CourseName", cnf.CourseName);
        }


        private static readonly Regex sWhitespace_and_pause = new Regex(@"[\s-]+");
        private static string RemoveWhitespacesAndPauses(string input)
        {
            return sWhitespace_and_pause.Replace(input, "");
        }

        private async Task run_with_retries(Func<Task> func, int n_retries)
        {
            bool was_exception = false;
            Exception exc = null;
            for (int i = 0; i < n_retries; ++i)
            {
                try
                {
                    await func();
                    was_exception = false;
                    break;
                }
                catch (AuthorizationException ex)
                {
                    throw ex;
                }
                catch (Exception ex)
                {
                    was_exception = true;
                    exc = ex;
                }
            }
            if (was_exception)
                throw exc;
        }


        private async Task<IEnumerable<string>> GetTeamsNames()
        {
            List<string> res = new List<string>();
            IReadOnlyList<Team> teams = null;// await client.Organization.Team.GetAll(organization);

            await run_with_retries(async () =>
            {
                teams = await client.Organization.Team.GetAll(organization);
            }, expcetion_retries);

            foreach (var t in teams)
                res.Add(t.Name);
            return res;
        }

        private string ImproveTeamName(string team_name, IEnumerable<string> existing_teams)
        {
            if (!existing_teams.Contains(team_name))
                return team_name;

            int suffix = 1;

            string res;
            do
            {
                res = team_name + '-' + suffix;
                suffix++;
            } while (existing_teams.Contains(res));
            return res;

        }

        public ActionResult RepoToRepo()
        {
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                client.Credentials = new Credentials(accessToken);
            }
            else
                return Redirect(GetOauthLoginUrl());

            return View();
        }

        private void writeLogPart(int indent, List<string> log, string message)
        {
            string elem = "";
            for (int i = 0; i < indent; ++i)
                elem += "&nbsp;";

            log.Add(elem + message + "<br \\>");
        }

        private async Task copyRepo(string src_repo, string dest_repo, string src_dir, string dest_dir, int indent, List<string> log)
        {
            IReadOnlyList<RepositoryContent> contents = null;
            await run_with_retries(async () =>
            {
                contents = await client.Repository.Content.GetAllContents(organization, src_repo, src_dir);
            }, expcetion_retries);
            //foreach (var item in await client.Repository.Content.GetAllContents(organization, src_repo, src_dir))
            foreach (var item in contents)
            {
                if (item.Type == ContentType.Dir)
                {
                    //await copyRepo(src_repo, dest_repo, src_dir + "/" + item.Name, dest_dir + "/" + item.Name, indent + 3, log);
                    await copyRepo(src_repo, dest_repo, src_dir + "/" + item.Name, dest_dir + "/" + item.Name, indent + 3, log);                    
                }
                else
                {
                    try
                    {
                        //var file = await client.Repository.Content.GetAllContents(organization, src_repo, src_dir + "/" + item.Name);
                        IReadOnlyList<RepositoryContent> file = null;
                        await run_with_retries(async () =>
                        {
                            file = await client.Repository.Content.GetAllContents(organization, src_repo, src_dir + "/" + item.Name);
                        }, expcetion_retries);
                        writeLogPart(indent, log, "<span style='font-size:1.5em;'><span style='color:green;'>Copy</span><span style='color:gray;'> \"" + src_repo + "/" + src_dir + "/" + item.Name + "\"</span><span style='color:green;'> to </span><span style='color:gray;'>\"" + dest_repo + "/" + dest_dir + "\" </span></span>");

                        //await client.Repository.Content.CreateFile(organization, dest_repo, dest_dir + "/" + item.Name, new CreateFileRequest("Created By Teacher", file.First().Content));
                        await run_with_retries(async () =>
                        {
                            await client.Repository.Content.CreateFile(organization, dest_repo, dest_dir + "/" + item.Name, new CreateFileRequest("Created By Teacher", file.First().Content));
                        }, expcetion_retries);
                    }
                    catch (Exception ex)
                    {
                        writeLogPart(indent, log, "<span style='color:red'>Error: " + ex.ToString() + "</span>");
                    }
                }
            }
        }

        private async Task<List<string>> getAllReposForSection(string group_section)
        {
            //var tmp = await client.Repository.GetAllForOrg(organization);
            IReadOnlyList<Repository> tmp = null;
            await run_with_retries(async () =>
            {
                tmp = await client.Repository.GetAllForOrg(organization);
            }, expcetion_retries);
            
            return (from c in tmp where c.Name.Contains(group_section) select c.Name).ToList();
        }

        [HttpPost]
        public async Task<ActionResult> RepoToRepo(CopyModel model)
        {
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                client.Credentials = new Credentials(accessToken);
            }
            else
                return Redirect(GetOauthLoginUrl());

            //var repos = await getAllReposForSection("-gr" + model.Group + model.Section);
            List<string> repos = null;
            await run_with_retries(async () =>
            {
                repos = await getAllReposForSection("-gr" + model.Group + model.Section);
            }, expcetion_retries);

            List<string> log = new List<string>();
            foreach (var repo in repos)
                try
                {
                    //await copyRepo(model.SrcRepo, repo, model.SrcDir, model.DestDir, 0, log);
                    await copyRepo(model.SrcRepo, repo, model.SrcDir, model.DestDir, 0, log);
                }
                catch (Exception ex)
                {
                    log.Add("<span style='color:red'>Error: " + ex.ToString() + "</span>");
                }

            ViewBag.log = log;
            return View("RepoToRepoResult");

        }

        private async Task RemoveFromOrganization(string[] to_remove)
        {
            //var teams = await client.Organization.Team.GetAll(organization);
            IReadOnlyList<Team> teams = null;
            await run_with_retries(async () =>
            {
                teams = await client.Organization.Team.GetAll(organization);
            }, expcetion_retries);


            //var current_user_login = (await client.User.Current()).Login;
            string current_user_login = null;
            await run_with_retries(async () =>
            {
                current_user_login = (await client.User.Current()).Login;
            }, expcetion_retries);

            foreach (var team in teams)
            {
                if (to_remove.Contains(team.Name))
                {
                    string RandomName = team.Name.Split('-').First();
                   //var repos = await client.Organization.Team.GetAllRepositories(team.Id);
                   IReadOnlyList <Repository> repos = null;
                    await run_with_retries(async () =>
                    {
                        repos = await client.Organization.Team.GetAllRepositories(team.Id);
                    }, expcetion_retries);

                    foreach (var repo in repos)
                    {
                        //delete repo
                        //await client.Repository.Delete(organization, repo.Name);
                        await run_with_retries(async () =>
                        {
                            await client.Repository.Delete(organization, repo.Name);
                        }, expcetion_retries);
                    }
                    //var members = await client.Organization.Team.GetAllMembers(team.Id);
                    IReadOnlyList<Octokit.User> members = null;
                    await run_with_retries(async () =>
                    {
                        members = await client.Organization.Team.GetAllMembers(team.Id);
                    }, expcetion_retries);

                    foreach (var member in members)
                    {
                        if (current_user_login == member.Login)
                            continue;

                        // Never auto-remove org owners/admins.
                        bool isAdmin = false;
                        await run_with_retries(async () =>
                        {
                            var m = await client.Organization.Member.GetOrganizationMembership(organization, member.Login);
                            isAdmin = (m.Role.Value == MembershipRole.Admin);
                        }, expcetion_retries);
                        if (isAdmin)
                            continue;

                        //delete member
                        using (var dbContext = new GithubDataEntities())
                        {
                            var r = (from s in dbContext.Students where s.Course == cnf.courseEnumName && s.GithubLogin == member.Login select s).Count();                            
                            if (r > 1) //if user belongs to more than one team do not remove him/her from organization
                                continue;
                        }

                        //await client.Organization.Member.Delete(organization, member.Login);
                        await run_with_retries(async () =>
                        {
                            await client.Organization.Member.Delete(organization, member.Login);
                        }, expcetion_retries);

                    }

                    //delete team
                    //await client.Organization.Team.Delete(team.Id);
                    await run_with_retries(async () =>
                    {
                        await client.Organization.Team.Delete(team.Id);
                    }, expcetion_retries);


                    using (var dbContext = new GithubDataEntities())
                    {
                        var  r = from s in dbContext.Students where s.Course == cnf.courseEnumName && s.RandomName == RandomName select s;
                        dbContext.Students.Remove(r.First());
                        dbContext.SaveChanges();
                    }
                }
            }
        }

        [HttpPost]
        public async Task<ActionResult> RemoveStudents(string[] selectedItems)
        {
            if (selectedItems == null)
                return RedirectToAction("ManageRepositories");
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                client.Credentials = new Credentials(accessToken);

            }
            else
                return Redirect(GetOauthLoginUrl());

            try
            {
                await RemoveFromOrganization(selectedItems);
            }
            catch (Exception ex)
            {
                return Content(ex.ToString());
            }
            return View();
        }


        private async Task<string> GetIssuesForTeams(string[] _teams)
        {
            if (_teams == null)
                return "";
            string res = "";
            //var teams = await client.Organization.Team.GetAll(organization);
            IReadOnlyList<Team> teams = null;
            await run_with_retries(async () =>
            {
                teams = await client.Organization.Team.GetAll(organization);
            }, expcetion_retries);
            foreach (var team in teams)
            {
                if (_teams.Contains(team.Name))
                {
                    res += "**************************************************************************************************************************\n";
                    res += "Issues for team: " + team.Name + "\n";
                    string RandomName = team.Name.Split('-').First();
                    Student student=null;
                    using (var dbContext = new GithubDataEntities())
                    {
                        student = (from s in dbContext.Students where s.Course == cnf.courseEnumName && s.RandomName == RandomName select s).First();
                    }
                    res += student.Name + " " + student.Surname + "\n";
                    //var repos = await client.Organization.Team.GetAllRepositories(team.Id);
                    IReadOnlyList<Repository> repos = null;
                    await run_with_retries(async () =>
                    {
                        repos = await client.Organization.Team.GetAllRepositories(team.Id);
                    }, expcetion_retries);
                    foreach (var repo in repos)
                    {
                        //var issues = await client.Issue.GetAllForRepository(organization, repo.Name);
                        IReadOnlyList<Issue> issues = null;
                        await run_with_retries(async () =>
                        {
                            issues = await client.Issue.GetAllForRepository(organization, repo.Name);
                        }, expcetion_retries);                        
                        foreach (var issue in issues)
                        {
                            res += "--------------------------------------------------------------------------------------------------------------------------\n";
                            res += "Issue: " + issue.Number + "\n";
                            res += "Created by: " + issue.User.Login + "\n";
                            res += "Data utworzenia: " + issue.CreatedAt + "\n";
                            res += "Tytuł:\n" + issue.Title + "\n";
                            res += "Treść:\n" + issue.Body + "\n";
                            //var comments = await client.Issue.Comment.GetAllForIssue(organization, repo.Name, issue.Number);
                            IReadOnlyList<IssueComment> comments = null;
                            await run_with_retries(async () =>
                            {
                                comments = await client.Issue.Comment.GetAllForIssue(organization, repo.Name, issue.Number);
                            }, expcetion_retries);
                            res += "Komentarze: \n";
                            foreach (var comment in comments)
                            {
                                res += "Id: " + comment.Id + "\n";
                                res += "Data: " + comment.CreatedAt + "\n";
                                res += "Autor: " + comment.User.Login + "\n";
                                res += "Treść:\n" + comment.Body + "\n";
                                res += "\n\n";
                            }

                        }
                    }
                }
            }
            return res;
        }

        private async Task<string> GetCloneScriptForTeams(string[] _teams)
        {
            if (_teams == null)
                return "";
            string res = "";
            //var teams = await client.Organization.Team.GetAll(organization);
            IReadOnlyList<Team> teams = null;
            await run_with_retries(async () =>
            {
                teams = await client.Organization.Team.GetAll(organization);
            }, expcetion_retries);

            foreach (var team in teams)
            {
                if (_teams.Contains(team.Name))
                {
                    //var repos = await client.Organization.Team.GetAllRepositories(team.Id);
                    IReadOnlyList<Repository> repos = null;
                    await run_with_retries(async () =>
                    {
                        repos = await client.Organization.Team.GetAllRepositories(team.Id);
                    }, expcetion_retries);

                    string RandomName = team.Name.Split('-').First();
                    Student student = null;
                    using(var dbContext = new GithubDataEntities())
                    {
                        var r = from s in dbContext.Students where s.Course == cnf.courseEnumName && s.RandomName == RandomName select s;
                        student = r.First();
                    }

                    foreach (var repo in repos)
                    {
                        res += "git clone " + repo.CloneUrl.Replace("https://", "https://" + Session["OAuthToken"] + "@") + " \"" + student.Surname.RemoveDiacritics() + "-" + student.Name.RemoveDiacritics() + "-gr-" + student.Gr + student.Sec + "-" + RandomName + "\"\n";
                    }
                }
            }
            return res;
        }

        [HttpPost]
        public async Task<ActionResult> DownloadIssues(string[] selectedItems)
        {
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                client.Credentials = new Credentials(accessToken);
            }
            else
                return Redirect(GetOauthLoginUrl());
            string content = await GetIssuesForTeams(selectedItems);

            //return File(Encoding.UTF8.GetBytes(content), "text/plain", "issues.txt");
            return File(Encoding.UTF8.GetBytes(content), "text/plain", "issues-" + cnf.courseKey + ".txt");

            //var cd = new System.Net.Mime.ContentDisposition
            //{
            //    FileName = "issues.txt",
            //    Inline = false
            //};
            //Response.AppendHeader("Content-Disposition", cd.ToString());

            //byte[] res = new byte[content.Length * sizeof(char)];
            //System.Buffer.BlockCopy(content.ToCharArray(), 0, res, 0, content.Length * sizeof(char));
            //return File(res, System.Net.Mime.MediaTypeNames.Text.Plain);
        }


        [HttpPost]
        public async Task<ActionResult> GetCloneScriptSh(string[] selectedItems)
        {
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
                client.Credentials = new Credentials(accessToken);
            else
                return Redirect(GetOauthLoginUrl());

            string content = await GetCloneScriptForTeams(selectedItems);
            content = content.Replace("\r\n", "\n");   // .sh: LF line endings
            return File(Encoding.UTF8.GetBytes(content), "text/plain", "clone-" + cnf.courseKey + ".sh");
        }

        [HttpPost]
        public async Task<ActionResult> GetCloneScriptBat(string[] selectedItems)
        {
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
                client.Credentials = new Credentials(accessToken);
            else
                return Redirect(GetOauthLoginUrl());

            string content = await GetCloneScriptForTeams(selectedItems);
            content = content.Replace("\r\n", "\n").Replace("\n", "\r\n");   // .bat: CRLF line endings
            return File(Encoding.UTF8.GetBytes(content), "text/plain", "clone-" + cnf.courseKey + ".bat");
        }

        public ActionResult AddFromCSV()
        {
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                client.Credentials = new Credentials(accessToken);
            }
            else
                return Redirect(GetOauthLoginUrl());

            return View();
        }

        [HttpPost]
        public async Task<ActionResult> AddFromCSV(HttpPostedFileBase csvFile)
        {
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                client.Credentials = new Credentials(accessToken);
            }
            else
                return Redirect(GetOauthLoginUrl());

            string logMessage = "";

            if (csvFile == null)
            {
                logMessage = "<span style='color:red'>Error: " + "Brak pliku" + "</span><br \\>\n";
                ViewBag.logMessage = logMessage;
                return View("AddFromCSVResult");
            }                
            else
            {
                // Read CSV bytes, then pick encoding: BOM (UTF-8/UTF-16) -> strict UTF-8 -> Windows-1250 fallback.
                // (Polish CSVs from Excel are usually Windows-1250; UTF-8 files, with or without BOM, also work.)
                byte[] csvBytes;
                using (var ms = new System.IO.MemoryStream())
                {
                    csvFile.InputStream.CopyTo(ms);
                    csvBytes = ms.ToArray();
                }

                string result;
                if (csvBytes.Length >= 3 && csvBytes[0] == 0xEF && csvBytes[1] == 0xBB && csvBytes[2] == 0xBF)
                {
                    result = Encoding.UTF8.GetString(csvBytes, 3, csvBytes.Length - 3);            // UTF-8 BOM
                }
                else if (csvBytes.Length >= 2 && csvBytes[0] == 0xFF && csvBytes[1] == 0xFE)
                {
                    result = Encoding.Unicode.GetString(csvBytes, 2, csvBytes.Length - 2);          // UTF-16 LE BOM
                }
                else if (csvBytes.Length >= 2 && csvBytes[0] == 0xFE && csvBytes[1] == 0xFF)
                {
                    result = Encoding.BigEndianUnicode.GetString(csvBytes, 2, csvBytes.Length - 2); // UTF-16 BE BOM
                }
                else
                {
                    try
                    {
                        // Strict UTF-8: throws if the bytes aren't valid UTF-8.
                        result = new UTF8Encoding(false, true).GetString(csvBytes);
                    }
                    catch (DecoderFallbackException)
                    {
                        // Not valid UTF-8 -> assume Windows-1250 (Polish Excel default).
                        result = Encoding.GetEncoding(1250).GetString(csvBytes);
                    }
                }

                result = result.Replace("\r", "");
                var lines = result.Split('\n');

                int line_no = 0;
                List<StudentData> parsedStudents = new List<StudentData>();
                foreach (var line in lines)
                {
                    ++line_no;
                    if (line == string.Empty)
                        continue;
                    var parts = line.Split(new char[] { ';', ',' });
                    if (parts.Length != 5)
                    {
                        
                        logMessage += "<span style='color:red'>Blad w linii " + line_no + ": " + line + " </span><br \\>\n";                        
                    }

                    StudentData s = new StudentData();                    
                    s.GitLogin = parts[0];
                    s.FirstName = parts[1];
                    s.Surname = parts[2];
                    int gr, sec;
                    if(!int.TryParse(parts[3], out gr) || !int.TryParse(parts[4], out sec))
                    {                        
                        logMessage += "<span style='color:red'>Blad w linii " + line_no + ": " + line + " (nie mozna sparsowac numeru grupy lub sekcji)" + " </span><br \\>\n";
                        break;
                    }
                    if(gr < 0 || gr > 9 || sec < 0 || sec > 9)
                    {
                        logMessage += "<span style='color:red'>Blad w linii " + line_no + ": " + line + " (bledny numer grupy lub sekcji)" + " </span><br \\>\n";                                            
                    }
                    s.Group = gr;
                    s.Section = sec;
                    parsedStudents.Add(s);
                }
                if (logMessage != string.Empty) //any error
                {
                    ViewBag.logMessage = logMessage;
                    return View("AddFromCSVResult");
                }

                var resultsTasks = new List<Task<string>>();                

                foreach (var s in parsedStudents)              
                    resultsTasks.Add(addStudentImplParallel(s));

                
                foreach (var task in resultsTasks)
                    logMessage += await task;

                //nie do konca kumam jak to dziala ale tak jak nizej nie mozna bo sie blokuje tylko trzeba zrobic tak z await jak robie wyzej...
                //Task.WaitAll(results.ToArray());

                
                ViewBag.logMessage = logMessage;
                return View("AddFromCSVResult");
            }            
        }
        public async Task<ActionResult> Index()
        {            
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                client.Credentials = new Credentials(accessToken);
            }
            else
                return Redirect(GetOauthLoginUrl());
            
            try
            {
                //var repositories = await client.Repository.GetAllForCurrent();
                //IReadOnlyList<Repository> repositories = null;
                //await run_with_retries(async () =>
                //{
                //    repositories = await client.Repository.GetAllForCurrent();
                //}, expcetion_retries);
                return View();
            }
            catch (AuthorizationException)
            {
                return Redirect(GetOauthLoginUrl());
            }
            //catch (Exception)
            //{
            //    return Redirect(GetOauthLoginUrl());
            //}
        }
        private void FixFirstNameAndSurname(ref string FirstName, ref string Surname)
        {
            if(FirstName != null)
            { 
                FirstName = FirstName.Trim().Replace("-", "");
                FirstName = FirstName[0].ToString().ToUpper() + FirstName.Substring(1).ToLower();
            }
            if (Surname != null)
            { 
                Surname = Surname.Trim().Replace("-", "");
                Surname = Surname[0].ToString().ToUpper() + Surname.Substring(1).ToLower();
            }
        }

        private void FixFirstNameAndSurname(StudentData student)
        {
            string FirstName = student.FirstName;
            string Surname = student.Surname;
            FixFirstNameAndSurname(ref FirstName, ref Surname);
            student.FirstName = FirstName;
            student.Surname = Surname;
        }
        private void FixFirstNameAndSurname(ManageReposModel model)
        {
            string FirstName = model.FirstName;
            string Surname = model.Surname;
            FixFirstNameAndSurname(ref FirstName, ref Surname);
            model.FirstName = FirstName;
            model.Surname = Surname;
        }

        private async Task<CreateResult> addStudentImpl(StudentData student)
        {
            FixFirstNameAndSurname(student);

            CreateResult res = new CreateResult();

            string RandomName = "";
            Team team_id = null;

            await run_with_retries(async () =>
            {
                RandomName = Guid.NewGuid().ToString("N").Substring(0, 8);
                res.TeamName = RandomName + "-gr" + student.Group + student.Section;
                team_id = await client.Organization.Team.Create(organization, new NewTeam(res.TeamName) { Permission = TeamPermission.Push });
            }, expcetion_retries);

            res.RepoName = res.TeamName + "-repo";

            await run_with_retries(async () =>
            {
                await client.Organization.Team.AddOrEditMembership(team_id.Id, student.GitLogin, new UpdateTeamMembership(TeamRole.Member));
            }, expcetion_retries);

            const string TEMPLATE_REPO_NAME = "_TEMPLATE_DO_NOT_DELETE";

            try
            {
                await run_with_retries(async () =>
                {
                    await client.Repository.Generate(organization, TEMPLATE_REPO_NAME, new NewRepositoryFromTemplate(res.RepoName) { Private = true, Owner = organization });
                }, expcetion_retries);
            }
            catch (Octokit.NotFoundException ex)
            {
                throw new Exception(
                        "Nie znaleziono repozytorium-szablonu '" + TEMPLATE_REPO_NAME + "' w organizacji '" +
                        organization + "'. Utwórz je (z odpowiednią strukturą katalogów) i oznacz jako " +
                        "'Template repository' w ustawieniach repozytorium. Szczegóły: " + ex.ToString());
            }

            await run_with_retries(async () =>
            {
                await client.Organization.Team.AddRepository(team_id.Id, organization, res.RepoName);
            }, expcetion_retries);

            using (var dbContext = new GithubDataEntities())
            {
                dbContext.Students.Add(new Student
                {
                    Name = student.FirstName,
                    Surname = student.Surname,
                    Course = cnf.courseEnumName,
                    RandomName = RandomName,
                    Gr = student.Group,
                    Sec = student.Section,
                    GithubLogin = student.GitLogin,
                    CreateDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
                dbContext.SaveChanges();
            }

            return res;
        }

        private async Task<string> addStudentImplParallel(StudentData s)
        {
            string msg = "";
            try
            {
                var res = await addStudentImpl(s);
                msg += "<span style='color:green'>Dodano " + s.FirstName + " " + s.Surname + ". Repo name: " + res.RepoName + ", team name: " + res.TeamName + "</span><br \\>\n";
            }
            catch (Exception ex)
            {
                msg += "Blad przy dodawaniu " + s.FirstName + " " + s.Surname + "<br />\n";
                msg += "<span style='color:red'>Blad przy dodawaniu " + s.FirstName + " " + s.Surname + ": " + ex.ToString() + " </span><br \\>\n";
            }
            return msg;
        }

        [HttpPost]
        public async Task<ActionResult> Index(StudentData student)
        {
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                client.Credentials = new Credentials(accessToken);
            }
            else
                return Redirect(GetOauthLoginUrl());

            try
            {
                var res = await addStudentImpl(student);
                return View("CreateResult", res);
            }
            catch (Exception ex)
            {
                return Content(ex.ToString());
            }
            
        }

        public ActionResult ManageRepositories()
        {
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                client.Credentials = new Credentials(accessToken);
            }
            else
                return Redirect(GetOauthLoginUrl());
            return View();
        }

        [HttpPost]
        public async Task<ActionResult> ManageRepositories(ManageReposModel model)
        {
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                client.Credentials = new Credentials(accessToken);
            }
            else
                return Redirect(GetOauthLoginUrl());
            
            try
            {
                List<ManageModel> res = new List<ManageModel>();
                using (var dbContext = new GithubDataEntities())
                {
                    /*
                     var r = from s in dbContext.Students
                            where
                            (string.IsNullOrEmpty(model.FirstName) || s.Name.ToLower().RemoveDiacritics() == model.FirstName.ToLower().RemoveDiacritics()) &&
                            (string.IsNullOrEmpty(model.Surname) || s.Surname.ToLower().RemoveDiacritics() == model.Surname.ToLower().RemoveDiacritics()) &&
                            s.Course == course.ToString() &&
                            (model.Group == null || s.Gr == model.Group) &&
                            (model.Section == null || s.Sec == model.Section)
                            select s;
                    */

                    FixFirstNameAndSurname(model);

                    var _r = from s in dbContext.Students
                            where                            
                            s.Course == cnf.courseEnumName &&
                            (model.Group == null || s.Gr == model.Group) &&
                            (model.Section == null || s.Sec == model.Section)
                            select s;
                    var r = from s in _r.AsEnumerable()
                            where
                            (string.IsNullOrEmpty(model.FirstName) || s.Name.ToLower().RemoveDiacritics() == model.FirstName.ToLower().RemoveDiacritics()) &&
                            (string.IsNullOrEmpty(model.Surname) || s.Surname.ToLower().RemoveDiacritics() == model.Surname.ToLower().RemoveDiacritics())
                            select s;

                    foreach (var entry in r)
                    {
                        string repoName = entry.RandomName + "-gr" + entry.Gr + entry.Sec + "-repo";
                        Octokit.Repository repo = null;
                        try
                        {
                            repo = await client.Repository.Get(organization, repoName);
                        }
                        catch(Octokit.NotFoundException ex)
                        {
                            var tmp = 0;//to be able to put breakpoint
                        }

                        string teamName = entry.RandomName + "-gr" + entry.Gr + entry.Sec;
                        string clone_url_oauth = repo.CloneUrl.Replace("https://", "https://" + Session["OAuthToken"] + "@");
                        res.Add(new ManageModel
                        {
                            HtmlUrl = repo != null ? repo.HtmlUrl : "",
                            GithubLogin = entry.GithubLogin,
                            RandomName = entry.RandomName,
                            CloneUrl = clone_url_oauth,
                            Name = entry.Name,
                            Surname = entry.Surname,
                            Group = (int)entry.Gr,
                            Section = (int)entry.Sec,
                            TeamName = teamName
                        });
                    }
                    return View("ManageRepositoriesResult", res);
                }

              // string pattern = "";
              // if (model.FirstName != null)
              //     pattern += model.FirstName;
              // else
              //     pattern += ".*";
              // pattern += "-";
              // if (model.Surname != null)
              //     pattern += model.Surname;
              // else
              //     pattern += ".*";
              // pattern += "-gr";
              // if (model.Group != null)
              //     pattern += model.Group.ToString();
              // else
              //     pattern += ".";
              // if (model.Section != null)
              //     pattern += model.Section.ToString();
              // else
              //     pattern += ".";
              //
              // pattern += "-repo";
              //
              // List<ManageModel> res = new List<ManageModel>();
              //
              // //var tmp = await client.Repository.GetAllForOrg(organization);
              // IReadOnlyList<Repository> tmp = null;
              // await run_with_retries(async () =>
              // {
              //     tmp = await client.Repository.GetAllForOrg(organization);
              // }, expcetion_retries);
              //
              // foreach (var v in tmp)
              // {
              //     if (v.Name.Count(x => x == '-') != 3)
              //         continue;
              //     if (System.Text.RegularExpressions.Regex.Match(v.FullName, pattern).Success)
              //     {
              //         string clone_url_oauth = v.CloneUrl.Replace("https://", "https://" + Session["OAuthToken"] + "@");
              //         var splitted = v.Name.Split('-');
              //         var team = v.Name.Replace("-repo", "");
              //
              //         res.Add(new ManageModel
              //         {
              //             CloneUrl = clone_url_oauth,
              //             Name = splitted[0],
              //             Surname = splitted[1],
              //             Group = int.Parse(splitted[2].Substring(2, 1)),
              //             Section = int.Parse(splitted[2].Substring(3, 1)),
              //             TeamName = team
              //         });
              //     }
              // }
              // //return View("Index");                
              // return View("ManageRepositoriesResult", res);
            }
            catch (System.Exception ex)
            {
                return Content(ex.ToString());
            }

            
        }

        public ActionResult CreateIssue()
        {
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                client.Credentials = new Credentials(accessToken);
            }
            else
                return Redirect(GetOauthLoginUrl());
            return View();
        }


        [HttpPost]
        public async Task<ActionResult> CreateIssue(CreateIssueModel model)
        {
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                client.Credentials = new Credentials(accessToken);
            }
            else
                return Redirect(GetOauthLoginUrl());

            var repos = await getAllReposForSection("-gr" + model.Group + model.Section);
            ViewBag.Res = "Success";
            try
            {
                foreach (var repo in repos)
                {
                    //await client.Issue.Create(organization, repo, new NewIssue(model.Title) { Body = model.Content });
                    await run_with_retries(async () =>
                    {
                        await client.Issue.Create(organization, repo, new NewIssue(model.Title) { Body = model.Content });
                    }, expcetion_retries);
                }
            }
            catch (Exception)
            {
                ViewBag.Res = "Some Error occured";
            }
            return View("CreateIssueResult");
        }

        public async Task<ActionResult> Authorize(string code, string state)
        {
            if (!String.IsNullOrEmpty(code))
            {
                var expectedState = Session["CSRF:State"] as string;
                if (state != expectedState) throw new InvalidOperationException("SECURITY FAIL!");
                Session["CSRF:State"] = null;

                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12; //related to https://github.com/blog/2507-weak-cryptographic-standards-removed
                var token = await client.Oauth.CreateAccessToken(
                    new OauthTokenRequest(clientId, clientSecret, code));
                Session["OAuthToken"] = token.AccessToken;
            }

            return RedirectToAction("Index");
        }

        private string GetOauthLoginUrl()
        {
            string csrf = Membership.GeneratePassword(24, 1);
            Session["CSRF:State"] = csrf;

            var request = new OauthLoginRequest(clientId)
            {
                Scopes = { "user", "notifications", "admin:org", "repo", "delete_repo" },
                State = csrf
            };
            var oauthLoginUrl = client.Oauth.GetGitHubLoginUrl(request);
            return oauthLoginUrl.ToString();
        }

    }
}

