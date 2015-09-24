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
            res.TeamName = student.FirstName + '-' + student.Surname + '-' + student.Group + student.Section;
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
