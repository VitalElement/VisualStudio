﻿using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using GitHub.Extensions;
using GitHub.Primitives;
using LibGit2Sharp;

namespace GitHub.Services
{
    [Export(typeof(IGitClient))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class GitClient : IGitClient
    {
        readonly PushOptions pushOptions;
        readonly FetchOptions fetchOptions;

        [ImportingConstructor]
        public GitClient(IGitHubCredentialProvider credentialProvider)
        {
            pushOptions = new PushOptions { CredentialsProvider = credentialProvider.HandleCredentials };
            fetchOptions = new FetchOptions { CredentialsProvider = credentialProvider.HandleCredentials };
        }

        public IObservable<Unit> Push(IRepository repository, string branchName, string remoteName)
        {
            Guard.ArgumentNotEmptyString(branchName, nameof(branchName));
            Guard.ArgumentNotEmptyString(remoteName, nameof(remoteName));

            return Observable.Defer(() =>
            {
                if (repository.Head?.Commits != null && repository.Head.Commits.Any())
                {
                    var remote = repository.Network.Remotes[remoteName];
                    repository.Network.Push(remote, "HEAD", @"refs/heads/" + branchName, pushOptions);
                }
                return Observable.Return(Unit.Default);
            });
        }

        public IObservable<Unit> Fetch(IRepository repository, string remoteName)
        {
            Guard.ArgumentNotEmptyString(remoteName, nameof(remoteName));

            return Observable.Defer(() =>
            {
                var remote = repository.Network.Remotes[remoteName];
                repository.Network.Fetch(remote, fetchOptions);
                return Observable.Return(Unit.Default);
            });
        }

        public IObservable<Unit> Fetch(IRepository repository, string remoteName, params string[] refspecs)
        {
            Guard.ArgumentNotEmptyString(remoteName, nameof(remoteName));

            return Observable.Defer(() =>
            {
                var remote = repository.Network.Remotes[remoteName];
                repository.Network.Fetch(remote, refspecs, fetchOptions);
                return Observable.Return(Unit.Default);
            });
        }

        public IObservable<Unit> SetRemote(IRepository repository, string remoteName, Uri url)
        {
            Guard.ArgumentNotEmptyString(remoteName, nameof(remoteName));

            return Observable.Defer(() =>
            {
                repository.Config.Set("remote." + remoteName + ".url", url.ToString());
                repository.Config.Set("remote." + remoteName + ".fetch", "+refs/heads/*:refs/remotes/" + remoteName + "/*");

                return Observable.Return(Unit.Default);
            });
        }

        public IObservable<Unit> SetTrackingBranch(IRepository repository, string branchName, string remoteName)
        {
            Guard.ArgumentNotEmptyString(branchName, nameof(branchName));
            Guard.ArgumentNotEmptyString(remoteName, nameof(remoteName));

            return Observable.Defer(() =>
            {
                var remoteBranchName = IsCanonical(remoteName) ? remoteName : "refs/remotes/" + remoteName + "/" + branchName;
                var remoteBranch = repository.Branches[remoteBranchName];
                // if it's null, it's because nothing was pushed
                if (remoteBranch != null)
                {
                    var localBranchName = IsCanonical(branchName) ? branchName : "refs/heads/" + branchName;
                    var localBranch = repository.Branches[localBranchName];
                    repository.Branches.Update(localBranch, b => b.TrackedBranch = remoteBranch.CanonicalName);
                }
                return Observable.Return(Unit.Default);
            });
        }

        public IObservable<Remote> GetHttpRemote(IRepository repo, string remote)
        {
            return Observable.Defer(() => Observable.Return(GitService.GitServiceHelper.GetRemoteUri(repo, remote)))
                .Select(uri => new { Remote = uri.IsHypertextTransferProtocol ? remote : remote + "-http", Uri = uri })
                .Select(r =>
                {
                    var ret = repo.Network.Remotes[r.Remote];
                    if (ret == null)
                        ret = repo.Network.Remotes.Add(r.Remote, UriString.ToUriString(r.Uri.ToRepositoryUrl()));
                    return ret;
                });
        }

        static bool IsCanonical(string s)
        {
            return s.StartsWith("refs/", StringComparison.Ordinal);
        }
    }
}
