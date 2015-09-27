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

namespace StudentsToOrganization.Controllers
{
    public class HomeController : Controller
    {

        #region CONFIGURATION
        //your organization name here
        private const string organization = "";

        //from https://github.com/organizations/{your organization name}/settings/applications/new
        
        //ppk.marekkokot.com
        const string clientId = "";
        private const string clientSecret = "";





        #endregion

        readonly GitHubClient client = new GitHubClient(new ProductHeaderValue("StudentsToOrganization"));

        private async Task<IEnumerable<string>> GetTeamsNames()
        {
            List<string> res = new List<string>();
            var teams = await client.Organization.Team.GetAll(organization);
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
           
            foreach (var item in await client.Repository.Content.GetAllContents(organization, src_repo, src_dir))
            {
                if (item.Type == ContentType.Dir)
                    await copyRepo(src_repo, dest_repo, src_dir + "/" + item.Name, dest_dir + "/" + item.Name, indent + 3, log);
                else
                {
                    try
                    {
                        var file = await client.Repository.Content.GetAllContents(organization, src_repo, src_dir + "/" + item.Name);
                        writeLogPart(indent, log, "<span style='font-size:1.5em;'><span style='color:green;'>Copy</span><span style='color:gray;'> \"" + src_repo + "/" + src_dir + "/" + item.Name + "\"</span><span style='color:green;'> to </span><span style='color:gray;'>\"" + dest_repo + "/" + dest_dir + "\" </span></span>");
                        await client.Repository.Content.CreateFile(organization, dest_repo, dest_dir + "/" + item.Name, new CreateFileRequest("Created By Teacher", file.First().Content));
                    }
                    catch(Exception ex)
                    {
                        writeLogPart(indent, log, "<span style='color:red'>Error: " + ex.ToString() + "</span>");
                    }
                }
            }            
        }

        private async Task<List<string>> getAllReposForSection(string group_section)
        {
            var tmp = await client.Repository.GetAllForOrg(organization);
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

            var repos = await getAllReposForSection("-gr" + model.Group + model.Section);
            List<string> log = new List<string>();
            foreach (var repo in repos)
                try
                {
                    await copyRepo(model.SrcRepo, repo, model.SrcDir, model.DestDir, 0, log);
                }
                catch (Exception ex)
                {
                    log.Add("<span style='color:red'>Error: " + ex.ToString() + "</span>");
                }

            ViewBag.log = log;
            return View("RepoToRepoResult");

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
                var repositories = await client.Repository.GetAllForCurrent();
                return View();
            }
            catch (AuthorizationException)
            {
                return Redirect(GetOauthLoginUrl());
            }
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

            student.FirstName = student.FirstName[0].ToString().ToUpper() + student.FirstName.Substring(1).ToLower();
            student.Surname = student.Surname[0].ToString().ToUpper() + student.Surname.Substring(1).ToLower();


            CreateResult res = new CreateResult();
            res.TeamName = student.FirstName + '-' + student.Surname + "-gr" + student.Group + student.Section;
            res.TeamName = res.TeamName.RemoveDiacritics();
            try
            {
                res.TeamName = ImproveTeamName(res.TeamName, await GetTeamsNames());

                res.RepoName = res.TeamName + "-repo";

                var team_id = await client.Organization.Team.Create(organization, new NewTeam(res.TeamName) { Permission = Permission.Push });
                await client.Organization.Team.AddMembership(team_id.Id, student.GitLogin);
                await client.Repository.Create(organization, new NewRepository(res.RepoName) { Private = true, AutoInit = true, GitignoreTemplate = "VisualStudio", TeamId = team_id.Id });

                await client.Repository.Content.CreateFile(organization, res.RepoName, "laboratorium/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne"));
                await client.Repository.Content.CreateFile(organization, res.RepoName, "projekt 1/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać projekt 1"));
                await client.Repository.Content.CreateFile(organization, res.RepoName, "projekt 2/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać projekt 2"));
            }
            catch (Exception ex)
            {
                return Content(ex.ToString());
            }
            return View("CreateResult", res);
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
                    await client.Issue.Create(organization, repo, new NewIssue(model.Title) { Body = model.Content });
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
                Scopes = { "user", "notifications", "admin:org", "repo" },
                State = csrf
            };
            var oauthLoginUrl = client.Oauth.GetGitHubLoginUrl(request);
            return oauthLoginUrl.ToString();
        }

    }
}
