﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Toggl;
using Toggl.DataObjects;
using Toggl.Extensions;
using Toggl.QueryObjects;
using Toggl.Services;

namespace togglhelper
{
    public class Toggl
    {
        private readonly WorkspaceService _workspaceService;
        private readonly ClientService _clientService;
        private readonly ProjectService _projectService;
        private readonly TimeEntryService _timeEntryService;
        private readonly ReportService _reportService;
        private Workspace _workspace;
        private List<Client> _clients;

        public Toggl(string apiToken)
        {
            var apiService = new ApiService(apiToken);
            _workspaceService = new WorkspaceService(apiService);
            _clientService = new ClientService(apiService);
            _projectService = new ProjectService(apiService);
            _timeEntryService = new TimeEntryService(apiService);
            _reportService = new ReportService(apiService);
        }

        public void Setup(string workspaceName, params string[] tfsProjectNames)
        {
            _workspace = _workspaceService.List().FirstOrDefault(f => f.Name == workspaceName);
            if (_workspace == null)
            {
                Console.WriteLine($"can't find workspace {workspaceName}");
                Environment.Exit(0);
            }

            _clients = _clientService.List();

            foreach (var tfsProjectName in tfsProjectNames)
            {
                var client = _clients.FirstOrDefault(f => f.Name == tfsProjectName && f.WorkspaceId == _workspace.Id);

                if (client != null) continue;

                client = _clientService.Add(new Client
                {
                    Name = tfsProjectName,
                    WorkspaceId = _workspace.Id
                });

                Console.WriteLine($"synced client {client.Name} in workspace {_workspace.Name}");
            }

            _clients = _clientService.List();
        }

        public void CreateProjectsIfNotExist(IEnumerable<WorkItem> tasks)
        {
            if (_workspace == null) return;

            var groupedWorkItems = tasks
                .GroupBy(s => s.Fields["System.TeamProject"] as string)
                .Distinct();

            foreach (var tfsProjectItems in groupedWorkItems)
            {
                var client = _clients.FirstOrDefault(f => f.Name == tfsProjectItems.Key && f.WorkspaceId == _workspace.Id) ?? _clientService.Add(new Client
                {
                    Name = tfsProjectItems.Key,
                    WorkspaceId = _workspace.Id
                });

                Console.WriteLine($"found {tfsProjectItems.Count()} workitems for client {client.Name}");

                var projects = _projectService.List();
                foreach (var task in tfsProjectItems)
                {

                    var project = projects.FirstOrDefault(f => f.Name.Contains(task.Id.ToString()));

                    if (project != null) continue;

                    project = _projectService.Add(new Project
                    {
                        Id = task.Id,
                        WorkspaceId = _workspace.Id,
                        ClientId = client.Id,
                        Name = $"#{task.Id} - {task.Fields["System.Title"]}"
                    });

                    Console.WriteLine($"synced project {project.Name} for client {client.Name} in workspace {_workspace.Name}");
                }
            }
        }

        public IList<ReportTimeEntry> GetTimeEntries(int daysBack)
        {
            var result = new List<ReportTimeEntry>();
            foreach (var workspace in _workspaceService.List())
            {
                if (!workspace.Id.HasValue) continue;

                var reportPage = _reportService.FullDetailedReport(new DetailedReportParams
                {
                    UserAgent = "TogglReporter",
                    WorkspaceId = (int)workspace.Id,
                    Since = DateTime.Now.AddDays(daysBack).ToIsoDateStr()
                });

                result.AddRange(reportPage.Data);
            }

            return result;
        }

        public void CreateTimeEntry(string workspaceName, DateTime from, DateTime to, string description)
        {
            var _workspace = _workspaceService.List().FirstOrDefault(f => f.Name == workspaceName);
            var result = _timeEntryService.Add(new TimeEntry
            {
                WorkspaceId = _workspace.Id,
                CreatedWith = "togglhelper",
                Start = from.ToIsoDateStr(),
                Stop = to.ToIsoDateStr(),
                Duration = (long)to.Subtract(from).TotalSeconds,
                Description = description
            });
            if (result != null)
            {
                Console.WriteLine($"created time entry \"{description}\" for {to.Subtract(from).TotalHours} hours at {from.ToShortDateString()}");
            }
        }
    }
}
