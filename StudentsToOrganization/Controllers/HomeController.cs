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

namespace StudentsToOrganization.Controllers
{
    public class HomeController : Controller
    {
        #region CONFIGURATION
        enum Course { PPK, PK2, PK3, PK4, PPKt, PK2t, AiSDt };
        const Course course = Course.PPK;
        const bool localhost = true;
        class OrgConfig
        {
            public readonly string organization;
            public readonly string clientId;
            public readonly string clientSecret;
            public OrgConfig(Course course)
            {
                //SET
                //from https://github.com/organizations/{your organization name}/settings/applications/new
                organization = "org-name";
                clientId = "";
                clientSecret = "";
            }
        }
        static readonly OrgConfig cnf = new OrgConfig(course);
        readonly string organization = cnf.organization;
        readonly string clientId = cnf.clientId;
        readonly string clientSecret = cnf.clientSecret;
        //your organization name here

      

        const int expcetion_retries = 15;//octocit quite often throws exceptions...

        #endregion

        readonly GitHubClient client = new GitHubClient(new ProductHeaderValue("StudentsToOrganization"));
        

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
                    //var repos = await client.Organization.Team.GetAllRepositories(team.Id);
                    IReadOnlyList<Repository> repos = null;
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
                        //delete member
                        if (current_user_login != member.Login)
                        {
                            //await client.Organization.Member.Delete(organization, member.Login);
                            await run_with_retries(async () =>
                            {
                                await client.Organization.Member.Delete(organization, member.Login);
                            }, expcetion_retries);
                        }
                    }

                    //delete team
                    //await client.Organization.Team.Delete(team.Id);
                    await run_with_retries(async () =>
                    {
                        await client.Organization.Team.Delete(team.Id);
                    }, expcetion_retries);
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
                            res += "Treść:\n:" + issue.Body + "\n";
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
                    foreach (var repo in repos)
                    {
                        res += "git clone " + repo.CloneUrl.Replace("https://", "https://" + Session["OAuthToken"] + "@") + "\n";
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
            var cd = new System.Net.Mime.ContentDisposition
            {
                FileName = "issues.txt",
                Inline = false
            };
            Response.AppendHeader("Content-Disposition", cd.ToString());
            string content = await GetIssuesForTeams(selectedItems);

            byte[] res = new byte[content.Length * sizeof(char)];
            System.Buffer.BlockCopy(content.ToCharArray(), 0, res, 0, content.Length * sizeof(char));
            return File(res, System.Net.Mime.MediaTypeNames.Text.Plain);
        }


        [HttpPost]
        public async Task<ActionResult> GetCloneScript(string[] selectedItems)
        {
            var accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                client.Credentials = new Credentials(accessToken);
            }
            else
                return Redirect(GetOauthLoginUrl());
            var cd = new System.Net.Mime.ContentDisposition
            {
                FileName = "clone.sh",
                Inline = false
            };
            Response.AppendHeader("Content-Disposition", cd.ToString());
            string content = await GetCloneScriptForTeams(selectedItems);

            byte[] res = new byte[content.Length * sizeof(char)];
            System.Buffer.BlockCopy(content.ToCharArray(), 0, res, 0, content.Length * sizeof(char));
            return File(res, System.Net.Mime.MediaTypeNames.Text.Plain);
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
                IReadOnlyList<Repository> repositories = null;
                await run_with_retries(async () =>
                {
                    repositories = await client.Repository.GetAllForCurrent();
                }, expcetion_retries);
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

                //var team_id = await client.Organization.Team.Create(organization, new NewTeam(res.TeamName) { Permission = Permission.Push });
                Team team_id = null;
                await run_with_retries(async () =>
                {
                    team_id = await client.Organization.Team.Create(organization, new NewTeam(res.TeamName) { Permission = Permission.Push });
                }, expcetion_retries);
                //await client.Organization.Team.AddMembership(team_id.Id, student.GitLogin);
                await run_with_retries(async () =>
                {
                    await client.Organization.Team.AddMembership(team_id.Id, student.GitLogin);
                }, expcetion_retries);

                //await client.Repository.Create(organization, new NewRepository(res.RepoName) { Private = true, AutoInit = true, GitignoreTemplate = "VisualStudio", TeamId = team_id.Id });

                await run_with_retries(async () =>
                {
                    await client.Repository.Create(organization, new NewRepository(res.RepoName) { Private = true, AutoInit = true, GitignoreTemplate = "VisualStudio", TeamId = team_id.Id });
                }, expcetion_retries);

                //ppk inf
                if (course == Course.PPK)
                {
                    for (int i = 1; i <= 14; ++i)
                    {
                        string nr = i.ToString();
                        if (i < 10)
                            nr = "0" + nr;
                //    
                        await run_with_retries(async () =>
                        {
                            await client.Repository.Content.CreateFile(organization, res.RepoName, "Temat " + nr + "/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać pliki związane z tematem " + i.ToString()));
                        }, expcetion_retries);
                    }
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "Student/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj można umieszczać pliki nie związane z projektem ani laboratorium. Jest to swego rodzaju brudnopis"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "projekt 1/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać projekt 1"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "projekt 2/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać projekt 2"));
                    }, expcetion_retries);
                }
                //{
                else if (course == Course.AiSDt)
                {
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "projekt 1/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać projekt 1"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "projekt 2/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać projekt 2"));
                    }, expcetion_retries);
                }
                //pk2 ??
                else if (course == Course.PK2)
                {
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab1/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 1"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab2/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 2"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab3/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 3"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab4/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 4"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab5/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 5"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "Projekt/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne"));
                    }, expcetion_retries);
                }
                //{
                else if (course == Course.PK4)
                {
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab1/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 1"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab2/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 2"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab3/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 3"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab4/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 4"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab5/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 5"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab6/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 5"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab7/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 5"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "Projekt/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne"));
                    }, expcetion_retries);
                }
                //ppk tele
                else if (course == Course.PPKt)
                {
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "Laboratorium/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "Projekt/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać projekt"));
                    }, expcetion_retries);
                }
                //{
                else if (course == Course.PK2t)
                {
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab1/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 1"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab2/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 2"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab3/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 3"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab4/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 4"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab5/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 5"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "lab6/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne nr 6"));
                    }, expcetion_retries);
                    await run_with_retries(async () =>
                    {
                        await client.Repository.Content.CreateFile(organization, res.RepoName, "Projekt/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać projekt"));
                    }, expcetion_retries);
                }
                //pk3
                else if (course == Course.PK3)
                {
                await run_with_retries(async () =>
                {
                    await client.Repository.Content.CreateFile(organization, res.RepoName, "Laboratorium/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać zajęcia laboratoryjne"));
                }, expcetion_retries);
                await run_with_retries(async () =>
                {
                    await client.Repository.Content.CreateFile(organization, res.RepoName, "Projekt/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj umieszczać projekt"));
                }, expcetion_retries);
                await run_with_retries(async () =>
                {
                    await client.Repository.Content.CreateFile(organization, res.RepoName, "Student/README.md", new CreateFileRequest("Wprowadzenie", "Tutaj można umieszczać pliki nie związane z projektem ani laboratorium. Jest to swego rodzaju brudnopis"));
                }, expcetion_retries);
                }
                else
                {
                    throw new Exception("Unknown course ");
                }
            }
            catch (Exception ex)
            {
                return Content(ex.ToString());
            }
            return View("CreateResult", res);
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

                string pattern = "";
                if (model.FirstName != null)
                    pattern += model.FirstName;
                else
                    pattern += ".*";
                pattern += "-";
                if (model.Surname != null)
                    pattern += model.Surname;
                else
                    pattern += ".*";
                pattern += "-gr";
                if (model.Group != null)
                    pattern += model.Group.ToString();
                else
                    pattern += ".";
                if (model.Section != null)
                    pattern += model.Section.ToString();
                else
                    pattern += ".";

                pattern += "-repo";

                List<ManageModel> res = new List<ManageModel>();

                //var tmp = await client.Repository.GetAllForOrg(organization);
                IReadOnlyList<Repository> tmp = null;
                await run_with_retries(async () =>
                {
                    tmp = await client.Repository.GetAllForOrg(organization);
                }, expcetion_retries);

                foreach (var v in tmp)
                {
                    if (v.Name.Count(x => x == '-') != 3)
                        continue;
                    if (System.Text.RegularExpressions.Regex.Match(v.FullName, pattern).Success)
                    {
                        string clone_url_oauth = v.CloneUrl.Replace("https://", "https://" + Session["OAuthToken"] + "@");
                        var splitted = v.Name.Split('-');
                        var team = v.Name.Replace("-repo", "");

                        res.Add(new ManageModel
                        {
                            CloneUrl = clone_url_oauth,
                            Name = splitted[0],
                            Surname = splitted[1],
                            Group = int.Parse(splitted[2].Substring(2, 1)),
                            Section = int.Parse(splitted[2].Substring(3, 1)),
                            TeamName = team
                        });
                    }
                }
                //return View("Index");                
                return View("ManageRepositoriesResult", res);
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
