﻿//
// Copyright 2020 Google LLC
//
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
//

using Google.Solutions.Common.Diagnostics;
using Google.Solutions.IapDesktop.Application.Controls;
using Google.Solutions.IapDesktop.Application.ObjectModel;
using Google.Solutions.IapDesktop.Application.Services.Integration;
using Google.Solutions.IapDesktop.Application.Views;
using Google.Solutions.IapDesktop.Application.Views.Dialog;
using Google.Solutions.IapDesktop.Application.Views.ProjectExplorer;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Google.Solutions.IapDesktop.Extensions.Os.Views.PackageInventory
{
    [SkipCodeCoverage("All logic in view model")]
    public partial class PackageInventoryWindow
        : ProjectExplorerTrackingToolWindow<PackageInventoryViewModel>
    {
        private readonly PackageInventoryViewModel viewModel;

        public PackageInventoryWindow(
            PackageInventoryType inventoryType,
            IServiceProvider serviceProvider)
            : base(
                  serviceProvider.GetService<IMainForm>().MainPanel,
                  serviceProvider.GetService<IProjectExplorer>(),
                  serviceProvider.GetService<IEventService>(),
                  serviceProvider.GetService<IExceptionDialog>())
        {
            this.components = new System.ComponentModel.Container();

            InitializeComponent();

            this.viewModel = new PackageInventoryViewModel(
                serviceProvider,
                inventoryType);


            this.infoLabel.BindProperty(
                c => c.Text,
                this.viewModel,
                m => m.InformationText,
                this.components);
            this.components.Add(this.viewModel.OnPropertyChange(
                m => m.IsInformationBarVisible,
                visible =>
                {
                    this.splitContainer.Panel1Collapsed = !visible;
                    this.splitContainer.SplitterDistance = this.splitContainer.Panel1MinSize;
                }));

            this.components.Add(this.viewModel.OnPropertyChange(
                m => m.WindowTitle,
                title => this.TabText = this.Text = title));

            // Bind list.
            this.packageList.BindProperty(
                c => c.Enabled,
                this.viewModel,
                m => m.IsPackageListEnabled,
                this.components);

            this.packageList.List.BindCollection(this.viewModel.FilteredPackages);
            this.packageList.List.AddCopyCommands();
            this.packageList.BindProperty(
                c => c.SearchTerm,
                this.viewModel,
                m => m.Filter,
                this.components);
            this.packageList.BindProperty(
                c => c.Loading,
                this.viewModel,
                m => m.IsLoading,
                this.components);


            var openUrl = new ToolStripMenuItem(
                "&Additional information...",
                null,
                (sender, args) =>
                {
                    using (Process.Start(new ProcessStartInfo()
                    {
                        UseShellExecute = true,
                        Verb = "open",
                        FileName = this.packageList.List.SelectedModelItem?.Package?.Weblink.ToString()
                    }))
                    { };
                });
            this.packageList.List.ContextMenuStrip.Items.Add(openUrl);
            this.packageList.List.ContextMenuStrip.Opening += (sender, args) =>
            {
                openUrl.Enabled = this.packageList.List.SelectedModelItem?.Package?.Weblink != null;
            };
        }

        //---------------------------------------------------------------------
        // ProjectExplorerTrackingToolWindow.
        //---------------------------------------------------------------------

        protected override async Task SwitchToNodeAsync(IProjectExplorerNode node)
        {
            Debug.Assert(!InvokeRequired, "running on UI thread");
            await this.viewModel.SwitchToModelAsync(node)
                .ConfigureAwait(true);
        }

        //---------------------------------------------------------------------
        // Window events.
        //---------------------------------------------------------------------

        private void PackageInventoryWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // NB. Hook KeyDown instead of KeyUp event to not interfere with 
            // child dialogs. With KeyUp, we'd get an event if a child dialog
            // is dismissed by pressing Enter.

            if ((e.Control && e.KeyCode == Keys.F) ||
                 e.KeyCode == Keys.F3)
            {
                this.packageList.SetFocusOnSearchBox();
            }
        }
    }
}
