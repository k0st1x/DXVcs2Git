﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DXVcs2Git.Core;
using NGitLab;
using NGitLab.Models;
using User = NGitLab.Models.User;

namespace DXVcs2Git.Git {
    public class GitLabWrapper {
        const string IgnoreValidation = "[IGNOREVALIDATION]";
        readonly GitLabClient client;
        public GitLabWrapper(string server, string token) {
            client = GitLabClient.Connect(server, token);
        }
        public bool IsAdmin() {
            return client.Users.Current.IsAdmin;
        }
        public IEnumerable<Project> GetProjects() {
            return client.Projects.Accessible;
        }
        public IEnumerable<Project> GetAllProjects() {
            return client.Projects.All;
        }
        public Project GetProject(int id) {
            return this.client.Projects[id];
        }
        public Project FindProject(string project) {
            return client.Projects.Accessible.FirstOrDefault(x => 
                string.Compare(x.HttpUrl, project, StringComparison.InvariantCultureIgnoreCase) == 0 ||
                string.Compare(x.SshUrl, project, StringComparison.InvariantCultureIgnoreCase) == 0);
        }
        public Project FindProjectFromAll(string project) {
            return client.Projects.All.FirstOrDefault(x => 
                string.Compare(x.HttpUrl, project, StringComparison.InvariantCultureIgnoreCase) == 0 ||
                string.Compare(x.SshUrl, project, StringComparison.InvariantCultureIgnoreCase) == 0);
        }
        public IEnumerable<MergeRequest> GetMergeRequests(Project project, Func<MergeRequest, bool> mergeRequestsHandler = null) {
            mergeRequestsHandler = mergeRequestsHandler ?? (x => true);
            var mergeRequestsClient = client.GetMergeRequest(project.Id);
            return mergeRequestsClient.AllInState(MergeRequestState.opened).Where(x => mergeRequestsHandler(x));
        }
        public MergeRequest GetMergeRequest(Project project, int id) {
            var mergeRequestsClient = client.GetMergeRequest(project.Id);
            return mergeRequestsClient[id];
        }
        public IEnumerable<MergeRequestFileData> GetMergeRequestChanges(MergeRequest mergeRequest) {
            var mergeRequestsClient = client.GetMergeRequest(mergeRequest.ProjectId);
            var changesClient = mergeRequestsClient.Changes(mergeRequest.Id);
            return changesClient.Changes.Files;
        }
        public IEnumerable<Commit> GetMergeRequestCommits(MergeRequest mergeRequest) {
            var mergeRequestsClient = client.GetMergeRequest(mergeRequest.ProjectId);
            return mergeRequestsClient.Commits(mergeRequest.Id).All;
        }
        public MergeRequest ProcessMergeRequest(MergeRequest mergeRequest, string comment) {
            var mergeRequestsClient = client.GetMergeRequest(mergeRequest.ProjectId);
            try {
                return mergeRequestsClient.Accept(mergeRequest.Id, new MergeCommitMessage() { Message = comment });
            }
            catch (Exception ex) {
                Log.Error("Merging has thrown exception", ex);
                mergeRequest.State = "merging_failed";
                return mergeRequest;
            }
        }
        public MergeRequest UpdateMergeRequestTitleAndDescription(MergeRequest mergeRequest, string title, string description) {
            var mergeRequestsClient = client.GetMergeRequest(mergeRequest.ProjectId);
            try {
                return mergeRequestsClient.Update(mergeRequest.Id, new MergeRequestUpdate() {
                    Description = description,
                    Title = title,
                    AssigneeId = mergeRequest.Assignee?.Id,
                    SourceBranch = mergeRequest.SourceBranch,
                    TargetBranch = mergeRequest.TargetBranch,
                });
            }
            catch {
                return mergeRequestsClient[mergeRequest.Id];
            }
        }
        public MergeRequest CloseMergeRequest(MergeRequest mergeRequest) {
            var mergeRequestsClient = client.GetMergeRequest(mergeRequest.ProjectId);
            try {
                return mergeRequestsClient.Update(mergeRequest.Id, new MergeRequestUpdate() {
                    NewState = "close",
                    AssigneeId = mergeRequest.Assignee?.Id,
                    SourceBranch = mergeRequest.SourceBranch,
                    TargetBranch = mergeRequest.TargetBranch,
                    Title = mergeRequest.Title,
                    Description = mergeRequest.Description,
                });
            }
            catch {
                return mergeRequestsClient[mergeRequest.Id];
            }
        }
        public MergeRequest CreateMergeRequest(Project origin, Project upstream, string title, string description, string user, string sourceBranch, string targetBranch) {
            var mergeRequestClient = this.client.GetMergeRequest(origin.Id);
            var mergeRequest = mergeRequestClient.Create(new MergeRequestCreate() {
                Title = title,
                Description = description,
                SourceBranch = sourceBranch,
                TargetBranch = targetBranch,
                TargetProjectId = upstream.Id,
            });
            return mergeRequest;
        }
        public MergeRequest ReopenMergeRequest(MergeRequest mergeRequest, string autoMergeFailedComment) {
            var mergeRequestsClient = client.GetMergeRequest(mergeRequest.ProjectId);
            try {
                return mergeRequestsClient.Update(mergeRequest.Id, new MergeRequestUpdate() {NewState = "reopen", Description = autoMergeFailedComment});
            }
            catch {
                return mergeRequestsClient[mergeRequest.Id];
            }
        }
        public User GetUser(int id) {
            return this.client.Users[id];
        }
        public IEnumerable<User> GetUsers() {
            var usersClient = this.client.Users;
            return usersClient.All.ToList();
        }
        public void RegisterUser(string userName, string displayName, string email) {
            try {
                var userClient = this.client.Users;
                var userUpsert = new UserUpsert() { Username = userName, Name = displayName, Email = email, Password = Guid.NewGuid().ToString(), ProjectsLimit = 10, Provider = null, ExternUid = null };
                userClient.Create(userUpsert);
            }
            catch (Exception ex) {
                Log.Error($"Can`t create user {userName} email {email}", ex);
                throw;
            }
        }
        public User RenameUser(User gitLabUser, string userName, string displayName, string email) {
            try {
                var userClient = this.client.Users;
                var userUpsert = new UserUpsert() { Username = userName, Name = displayName, Email = email, Password = new Guid().ToString(), ProjectsLimit = 10, Provider = null, ExternUid = null};
                return userClient.Update(gitLabUser.Id, userUpsert);
            }
            catch (Exception ex) {
                Log.Error($"Can`t change user {userName} email {email}", ex);
                throw;
            }
        }
        public Branch GetBranch(Project project, string branch) {
            var repo = this.client.GetRepository(project.Id);
            var branchesClient = repo.Branches;
            try {
                return branchesClient[branch];
            }
            catch {
                return branchesClient.All.FirstOrDefault(x => x.Name == branch);
            }
        }
        public IEnumerable<Branch> GetBranches(Project project) {
            var repo = this.client.GetRepository(project.Id);
            var branchesClient = repo.Branches;
            return branchesClient.All;
        }
        public MergeRequest UpdateMergeRequestAssignee(MergeRequest mergeRequest, string user) {
            var userInfo = GetUsers().FirstOrDefault(x => x.Username == user);
            if (mergeRequest.Assignee?.Username != userInfo?.Username) {
                var mergeRequestsClient = client.GetMergeRequest(mergeRequest.ProjectId);
                try {
                    return mergeRequestsClient.Update(mergeRequest.Id, new MergeRequestUpdate() {
                        AssigneeId = userInfo?.Id,
                        Title = mergeRequest.Title,
                        Description = mergeRequest.Description,
                        SourceBranch = mergeRequest.SourceBranch,
                        TargetBranch = mergeRequest.TargetBranch,
                    });
                }
                catch {
                    return mergeRequestsClient[mergeRequest.Id];
                }
            }
            return mergeRequest;
        }
        public Comment AddCommentToMergeRequest(MergeRequest mergeRequest, string comment) {
            var mergeRequestsClient = client.GetMergeRequest(mergeRequest.ProjectId);
            var commentsClient = mergeRequestsClient.Comments(mergeRequest.Id);
            return commentsClient.Add(new MergeRequestComment() { Note = comment });
        }
        public IEnumerable<ProjectHook> GetProjectHooks(Project project) {
            var repository = this.client.GetRepository(project.Id);
            return repository.ProjectHooks.All;
        }
        public ProjectHook FindProjectHook(Project project, Func<ProjectHook, bool> projectHookHandler) {
            var projectClient = client.GetRepository(project.Id);
            var projectHooks = projectClient.ProjectHooks;
            return projectHooks.All.FirstOrDefault(projectHookHandler);
        }
        public ProjectHook CreateProjectHook(Project project, Uri url, bool mergeRequestEvents, bool pushEvents, bool buildEvents) {
            var projectClient = client.GetRepository(project.Id);
            var projectHooks = projectClient.ProjectHooks;
            return projectHooks.Create(new ProjectHookUpsert() { MergeRequestsEvents = mergeRequestEvents, PushEvents = pushEvents, BuildEvents = buildEvents, Url = url, EnableSSLVerification = false});
        }
        public ProjectHook UpdateProjectHook(Project project, ProjectHook hook, Uri uri, bool mergeRequestEvents, bool pushEvents, bool buildEvents) {
            var repository = this.client.GetRepository(project.Id);
            return repository.ProjectHooks.Update(hook.Id, new ProjectHookUpsert() { Url = uri, MergeRequestsEvents = mergeRequestEvents, PushEvents = pushEvents, BuildEvents = buildEvents, EnableSSLVerification = false});
        }
        public IEnumerable<Comment> GetComments(MergeRequest mergeRequest) {
            var mergeRequestsClient = client.GetMergeRequest(mergeRequest.ProjectId);
            var commentsClient = mergeRequestsClient.Comments(mergeRequest.Id);
            return commentsClient.All;
        }
        public bool ShouldIgnoreSharedFiles(MergeRequest mergeRequest) {
            var mergeRequestsClient = client.GetMergeRequest(mergeRequest.ProjectId);
            var commentsClient = mergeRequestsClient.Comments(mergeRequest.Id);
            var comment = commentsClient.All.LastOrDefault();
            return comment?.Note == IgnoreValidation;
        }
        public IEnumerable<MergeRequestFileData> GetFileChanges(MergeRequest mergeRequest) {
            var mergeRequestsClient = client.GetMergeRequest(mergeRequest.ProjectId);
            var changes = mergeRequestsClient.Changes(mergeRequest.Id);
            return changes.Changes.Files;
        }
        public IEnumerable<Build> GetBuilds(MergeRequest mergeRequest, Sha1 sha) {
            var projectClient = client.GetRepository(mergeRequest.SourceProjectId);
            return projectClient.Builds.GetBuildsForCommit(sha);
        }
        public void ForceBuild(MergeRequest mergeRequest, Build build = null) {
            var projectClient = client.GetRepository(mergeRequest.SourceProjectId);
            var actualBuild = build ?? projectClient.Builds.GetBuilds().FirstOrDefault();
            if (actualBuild == null || actualBuild.Status == BuildStatus.success || actualBuild.Status == BuildStatus.pending || actualBuild.Status == BuildStatus.running)
                return;
            projectClient.Builds.Retry(actualBuild);
        }
        public void AbortBuild(MergeRequest mergeRequest, Build build) {
            var projectClient = client.GetRepository(mergeRequest.SourceProjectId);
            var actualBuild = build ?? projectClient.Builds.GetBuilds().FirstOrDefault();
            if (actualBuild == null || (actualBuild.Status != BuildStatus.pending && actualBuild.Status != BuildStatus.running))
                return;
            projectClient.Builds.Cancel(actualBuild);
        }
        public byte[] DownloadArtifacts(string projectUrl, Build build) {
            Func<string, Project> findProject = IsAdmin() ? (Func<string, Project>)FindProjectFromAll : FindProject;
            var project = findProject(projectUrl);
            if (project == null)
                return null;
            var projectClient = client.GetRepository(project.Id);
            return DownloadArtifactsCore(projectClient, build);
        }
        public byte[] DownloadArtifacts(MergeRequest mergeRequest, Build build) {
            var projectClient = client.GetRepository(mergeRequest.SourceProjectId);
            return DownloadArtifactsCore(projectClient, build);
        }
        public byte[] DownloadTrace(MergeRequest mergeRequest, Build build) {
            var projectClient = client.GetRepository(mergeRequest.SourceProjectId);
            byte[] result = null;
            projectClient.Builds.GetTraceFile(build, stream => {
                if (stream == null)
                    return;
                using (MemoryStream ms = new MemoryStream()) {
                    stream.CopyTo(ms);
                    result = ms.ToArray();
                }
            });
            return result;
        }
        static byte[] DownloadArtifactsCore(IRepositoryClient projectClient, Build build) {
            byte[] result = null;
            try {
                projectClient.Builds.GetArtifactFile(build, stream => {
                    if (stream == null)
                        return;
                    using (MemoryStream ms = new MemoryStream()) {
                        stream.CopyTo(ms);
                        result = ms.ToArray();
                    }
                });
            }
            catch (Exception ex) {
                Log.Error("Can`t download artifacts.", ex);
                return null;
            }
            return result;
        }
    }
}
