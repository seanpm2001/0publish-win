﻿/*
 * Copyright 2010-2016 Bastian Eicher
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser Public License for more details.
 *
 * You should have received a copy of the GNU Lesser Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using AeroWizard;
using JetBrains.Annotations;
using NanoByte.Common;
using NanoByte.Common.Storage;
using NanoByte.Common.Tasks;
using ZeroInstall.Publish.Capture;
using ZeroInstall.Publish.EntryPoints;
using ZeroInstall.Publish.Properties;
using ZeroInstall.Store;
using ZeroInstall.Store.Implementations.Archives;
using ZeroInstall.Store.Model;
using ZeroInstall.Store.Trust;
using Icon = System.Drawing.Icon;

namespace ZeroInstall.Publish.WinForms
{
    /// <summary>
    /// A wizard guiding the user through creating a new <see cref="Feed"/>.
    /// </summary>
    public sealed partial class NewFeedWizard : Form
    {
        private NewFeedWizard(IOpenPgp openPgp)
        {
            _openPgp = openPgp;

            InitializeComponent();
        }

        /// <summary>
        /// Runs the wizard.
        /// </summary>
        /// <param name="openPgp">Used to get a list of <see cref="OpenPgpSecretKey"/>s.</param>
        /// <param name="owner">The parent window the displayed window is modal to; can be <c>null</c>.</param>
        /// <returns>The feed generated by the wizard; <c>null</c> if the user canceled.</returns>
        [CanBeNull]
        public static SignedFeed Run([NotNull] IOpenPgp openPgp, [CanBeNull] IWin32Window owner = null)
        {
            using (var wizard = new NewFeedWizard(openPgp))
            {
                wizard.ShowDialog(owner);
                return wizard._signedFeed;
            }
        }

        /// <summary>Shared between wizard pages.</summary>
        private readonly FeedBuilder _feedBuilder = new FeedBuilder();

        /// <summary>Shared between installer-specific wizard pages.</summary>
        private readonly InstallerCapture _installerCapture = new InstallerCapture();

        #region pageDownload
        private void pageDownload_ToggleControls(object sender, EventArgs e)
        {
            groupLocalCopy.Enabled = checkLocalCopy.Checked;

            pageDownload.AllowNext =
                (textBoxDownloadUrl.Text.Length > 0) && textBoxDownloadUrl.IsValid &&
                (!checkLocalCopy.Checked || textBoxLocalPath.Text.Length > 0);
        }

        private void buttonSelectLocalPath_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog {FileName = textBoxLocalPath.Text})
            {
                if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                    textBoxLocalPath.Text = openFileDialog.FileName;
            }
        }

        private void pageDownload_Commit(object sender, WizardPageConfirmEventArgs e)
        {
            var fileName = checkLocalCopy.Checked ? textBoxLocalPath.Text : textBoxDownloadUrl.Text;

            try
            {
                if (fileName.EndsWithIgnoreCase(@".exe"))
                {
                    switch (Msg.YesNoCancel(this, Resources.AskInstallerEXE, MsgSeverity.Info, Resources.YesInstallerExe, Resources.NoSingleExecutable))
                    {
                        case DialogResult.Yes:
                            OnInstaller();
                            break;
                        case DialogResult.No:
                            OnSingleFile();
                            break;
                        default:
                            e.Cancel = true;
                            break;
                    }
                }
                else
                {
                    switch (Archive.GuessMimeType(fileName))
                    {
                        case Archive.MimeTypeMsi:
                            OnInstaller();
                            break;
                        case null:
                            OnSingleFile();
                            break;
                        default:
                            OnArchive();
                            break;
                    }
                }
            }
                #region Error handling
            catch (OperationCanceledException)
            {
                e.Cancel = true;
            }
            catch (ArgumentException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
            }
            catch (IOException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
            }
            catch (UnauthorizedAccessException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Error);
            }
            catch (WebException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
            }
            catch (NotSupportedException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
            }
            #endregion
        }

        private void OnSingleFile()
        {
            Retrieve(
                new SingleFile {Href = textBoxDownloadUrl.Uri},
                checkLocalCopy.Checked ? textBoxLocalPath.Text : null);
            _feedBuilder.ImplementationDirectory = _feedBuilder.TemporaryDirectory;
            using (var handler = new DialogTaskHandler(this))
            {
                _feedBuilder.DetectCandidates(handler);
                _feedBuilder.GenerateDigest(handler);
            }
            if (_feedBuilder.MainCandidate == null) throw new NotSupportedException(Resources.NoEntryPointsFound);
            else
            {
                _feedBuilder.GenerateCommands();
                pageDownload.NextPage = pageDetails;
            }
        }

        private void OnArchive()
        {
            Retrieve(
                new Archive {Href = textBoxDownloadUrl.Uri},
                checkLocalCopy.Checked ? textBoxLocalPath.Text : null);
            pageDownload.NextPage = pageArchiveExtract;
        }

        private void OnInstaller()
        {
            if (checkLocalCopy.Checked)
                _installerCapture.SetLocal(textBoxDownloadUrl.Uri, textBoxLocalPath.Text);
            else
            {
                using (var handler = new DialogTaskHandler(this))
                    _installerCapture.Download(textBoxDownloadUrl.Uri, handler);
            }

            pageDownload.NextPage = pageIstallerCaptureStart;
        }

        private void Retrieve(DownloadRetrievalMethod retrievalMethod, string localPath)
        {
            _feedBuilder.RetrievalMethod = retrievalMethod;

            using (var handler = new DialogTaskHandler(this))
            {
                _feedBuilder.TemporaryDirectory = (localPath == null)
                    ? retrievalMethod.DownloadAndApply(handler)
                    : retrievalMethod.LocalApply(localPath, handler);
            }
        }
        #endregion

        #region pageArchiveExtract
        private Archive _archive;

        private void pageArchiveExtract_Initialize(object sender, WizardPageInitEventArgs e)
        {
            _archive = (Archive)_feedBuilder.RetrievalMethod;

            listBoxExtract.BeginUpdate();
            listBoxExtract.Items.Clear();

            var baseDirectory = new DirectoryInfo(_feedBuilder.TemporaryDirectory);
            baseDirectory.Walk(dir => listBoxExtract.Items.Add(dir.RelativeTo(baseDirectory)));
            listBoxExtract.SelectedItem = baseDirectory.WalkThroughPrefix().RelativeTo(baseDirectory);

            listBoxExtract.EndUpdate();
        }

        private void pageArchiveExtract_Commit(object sender, WizardPageConfirmEventArgs e)
        {
            using (var handler = new DialogTaskHandler(this))
            {
                if (FileUtils.IsBreakoutPath(listBoxExtract.Text))
                {
                    e.Cancel = true;
                    Msg.Inform(this, Resources.ArchiveBreakoutPath, MsgSeverity.Error);
                    return;
                }

                _archive.Extract = listBoxExtract.Text ?? "";
                _feedBuilder.ImplementationDirectory = Path.Combine(_feedBuilder.TemporaryDirectory, FileUtils.UnifySlashes(_archive.Extract));

                try
                {
                    // Candidate detection is handled differently when capturing an installer
                    if (_installerCapture.CaptureSession == null)
                        _feedBuilder.DetectCandidates(handler);

                    _feedBuilder.GenerateDigest(handler);
                }
                    #region Error handling
                catch (OperationCanceledException)
                {
                    e.Cancel = true;
                    return;
                }
                catch (ArgumentException ex)
                {
                    e.Cancel = true;
                    Msg.Inform(this, ex.Message, MsgSeverity.Warn);
                    return;
                }
                catch (IOException ex)
                {
                    e.Cancel = true;
                    Msg.Inform(this, ex.Message, MsgSeverity.Warn);
                    return;
                }
                catch (UnauthorizedAccessException ex)
                {
                    e.Cancel = true;
                    Msg.Inform(this, ex.Message, MsgSeverity.Error);
                    return;
                }
                #endregion
            }

            if (_feedBuilder.ManifestDigest.PartialEquals(ManifestDigest.Empty))
            {
                Msg.Inform(this, Resources.EmptyImplementation, MsgSeverity.Warn);
                e.Cancel = true;
            }
            if (_feedBuilder.MainCandidate == null)
            {
                Msg.Inform(this, Resources.NoEntryPointsFound, MsgSeverity.Warn);
                e.Cancel = true;
            }
        }
        #endregion

        #region pageInstallerCaptureStart
        private void pageInstallerCaptureStart_Commit(object sender, WizardPageConfirmEventArgs e)
        {
            try
            {
                var captureSession = CaptureSession.Start(_feedBuilder);

                using (var handler = new DialogTaskHandler(this))
                    _installerCapture.RunInstaller(handler);

                _installerCapture.CaptureSession = captureSession;
            }
                #region Error handling
            catch (OperationCanceledException)
            {
                e.Cancel = true;
            }
            catch (IOException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
            }
            catch (UnauthorizedAccessException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
            }
            catch (InvalidOperationException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Error);
            }
            #endregion
        }

        private void buttonSkipCapture_Click(object sender, EventArgs e)
        {
            if (!Msg.YesNo(this, Resources.AskSkipCapture, MsgSeverity.Info)) return;

            try
            {
                using (var handler = new DialogTaskHandler(this))
                    _installerCapture.ExtractInstallerAsArchive(_feedBuilder, handler);
            }
                #region Error handling
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                Msg.Inform(this, Resources.InstallerExtractFailed + Environment.NewLine + ex.Message, MsgSeverity.Warn);
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                Msg.Inform(this, ex.Message, MsgSeverity.Error);
                return;
            }
            #endregion

            wizardControl.NextPage(pageArchiveExtract, skipCommit: true);
        }
        #endregion

        #region pageInstallerCaptureDiff
        private void buttonSelectInstallationDir_Click(object sender, EventArgs e)
        {
            using (var folderBrowserDialog = new FolderBrowserDialog
            {
                RootFolder = Environment.SpecialFolder.MyComputer,
                SelectedPath = textBoxInstallationDir.Text
            })
            {
                folderBrowserDialog.ShowDialog(this);
                textBoxInstallationDir.Text = folderBrowserDialog.SelectedPath;
            }
        }

        private void pageInstallerCaptureDiff_Commit(object sender, WizardPageConfirmEventArgs e)
        {
            var session = _installerCapture.CaptureSession;
            if (session == null) return;

            try
            {
                session.InstallationDir = textBoxInstallationDir.Text;
                using (var handler = new DialogTaskHandler(this))
                    session.Diff(handler);
            }
                #region Error handling
            catch (InvalidOperationException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
                return;
            }
            catch (IOException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
                return;
            }
            #endregion

            try
            {
                using (var handler = new DialogTaskHandler(this))
                    _installerCapture.ExtractInstallerAsArchive(_feedBuilder, handler);

                pageInstallerCaptureDiff.NextPage = pageArchiveExtract;
            }
            catch (IOException)
            {
                Msg.Inform(this, Resources.InstallerExtractFailed + Environment.NewLine + Resources.InstallerNeedAltSource, MsgSeverity.Info);
                pageInstallerCaptureDiff.NextPage = pageInstallerCollectFiles;
            }
                #region Error handling
            catch (OperationCanceledException)
            {
                e.Cancel = true;
            }
            catch (UnauthorizedAccessException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Error);
            }
            #endregion
        }

        private void pageInstallerCaptureDiff_Rollback(object sender, WizardPageConfirmEventArgs e)
        {
            _installerCapture.CaptureSession = null;
        }
        #endregion

        #region pageInstallerCollectFiles
        private void pageInstallerCollectFiles_ToggleControls(object sender, EventArgs e)
        {
            buttonCreateArchive.Enabled = (textBoxUploadUrl.Text.Length > 0) && textBoxUploadUrl.IsValid && (textBoxArchivePath.Text.Length > 0);
        }

        private void buttonSelectArchivePath_Click(object sender, EventArgs e)
        {
            string filter = StringUtils.Join(@"|",
                ArchiveGenerator.SupportedMimeTypes.Select(x => string.Format(
                    @"{0} archive (*{0})|*{0}",
                    Archive.GetDefaultExtension(x))));
            using (var saveFileDialog = new SaveFileDialog {Filter = filter, FileName = textBoxArchivePath.Text})
            {
                if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
                    textBoxArchivePath.Text = saveFileDialog.FileName;
            }
        }

        private void buttonCreateArchive_Click(object sender, EventArgs e)
        {
            try
            {
                using (var handler = new DialogTaskHandler(this))
                    _installerCapture.CaptureSession.CollectFiles(textBoxArchivePath.Text, textBoxUploadUrl.Uri, handler);
            }
                #region Error handling
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                Msg.Inform(this, ex.Message, MsgSeverity.Error);
                return;
            }
            #endregion

            wizardControl.NextPage(pageEntryPoint);
        }

        private void buttonExistingArchive_Click(object sender, EventArgs e)
        {
            wizardControl.NextPage(installerAltDownloadPage);
        }
        #endregion

        #region pageInstallerAltDownload
        private void pageInstallerAltDownload_ToggleControls(object sender, EventArgs e)
        {
            groupAltLocalCopy.Enabled = checkAltLocalCopy.Checked;

            installerAltDownloadPage.AllowNext =
                (textBoxAltDownloadUrl.Text.Length > 0) && textBoxAltDownloadUrl.IsValid &&
                (!checkAltLocalCopy.Checked || textBoxAltLocalPath.Text.Length > 0);
        }

        private void buttonSelectAltLocalPath_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog {FileName = textBoxAltLocalPath.Text})
            {
                if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                    textBoxAltLocalPath.Text = openFileDialog.FileName;
            }
        }

        private void pageInstallerAltDownload_Commit(object sender, WizardPageConfirmEventArgs e)
        {
            try
            {
                Retrieve(
                    new Archive {Href = textBoxAltDownloadUrl.Uri},
                    checkAltLocalCopy.Checked ? textBoxAltLocalPath.Text : null);
                installerAltDownloadPage.NextPage = pageArchiveExtract;
            }
                #region Error handling
            catch (OperationCanceledException)
            {
                e.Cancel = true;
            }
            catch (ArgumentException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
            }
            catch (IOException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
            }
            catch (UnauthorizedAccessException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Error);
            }
            catch (WebException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
            }
            catch (NotSupportedException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
            }
            #endregion
        }
        #endregion

        #region pageEntryPoint
        private void pageEntryPoint_Initialize(object sender, WizardPageInitEventArgs e)
        {
            listBoxEntryPoint.Items.Clear();
            listBoxEntryPoint.Items.AddRange(_feedBuilder.Candidates.Cast<object>().ToArray());
            listBoxEntryPoint.SelectedItem = _feedBuilder.MainCandidate;
        }

        private void pageEntryPoint_Commit(object sender, WizardPageConfirmEventArgs e)
        {
            _feedBuilder.MainCandidate = listBoxEntryPoint.SelectedItem as Candidate;
            if (_feedBuilder.MainCandidate == null)
            {
                e.Cancel = true;
                return;
            }

            if (_installerCapture.CaptureSession == null)
                _feedBuilder.GenerateCommands();
            else
                _installerCapture.CaptureSession.Finish(); // internally calls _feedBuilder.GenerateCommands()
        }
        #endregion

        #region pageDetails
        private void pageDetails_Initialize(object sender, WizardPageInitEventArgs e)
        {
            propertyGridCandidate.SelectedObject = _feedBuilder.MainCandidate;
        }

        private void pageDetails_Commit(object sender, WizardPageConfirmEventArgs e)
        {
            if (string.IsNullOrEmpty(_feedBuilder.MainCandidate.Name) || string.IsNullOrEmpty(_feedBuilder.MainCandidate.Summary) || _feedBuilder.MainCandidate.Version == null)
            {
                e.Cancel = true;
                Msg.Inform(this, labelDetails.Text, MsgSeverity.Warn);
            }
        }
        #endregion

        #region pageIcon
        private Icon _icon;

        private void pageIcon_Initialize(object sender, WizardPageInitEventArgs e)
        {
            pictureBoxIcon.Visible = buttonSaveIco.Enabled = buttonSavePng.Enabled = false;

            var iconContainer = _feedBuilder.MainCandidate as EntryPoints.IIconContainer;
            if (iconContainer != null)
            {
                try
                {
                    _icon = iconContainer.ExtractIcon();
                    pictureBoxIcon.Image = _icon.ToBitmap();
                }
                    #region Error handling
                catch (IOException ex)
                {
                    Msg.Inform(this, ex.Message, MsgSeverity.Warn);
                    return;
                }
                #endregion

                pictureBoxIcon.Visible = buttonSaveIco.Enabled = buttonSavePng.Enabled = true;
            }
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "System.Drawing exceptions are not clearly documented")]
        private void buttonSaveIco_Click(object sender, EventArgs e)
        {
            using (var saveFileDialog = new SaveFileDialog {Filter = "Windows Icon files|*.ico|All files|*.*"})
            {
                if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
                {
                    using (var stream = File.Create(saveFileDialog.FileName))
                    {
                        try
                        {
                            _icon.Save(stream);
                        }
                            #region Error handling
                        catch (Exception ex)
                        {
                            Msg.Inform(this, ex.Message, MsgSeverity.Warn);
                        }
                        #endregion
                    }
                }
            }
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "System.Drawing exceptions are not clearly documented")]
        private void buttonSavePng_Click(object sender, EventArgs e)
        {
            using (var saveFileDialog = new SaveFileDialog {Filter = "PNG image files|*.png|All files|*.*"})
            {
                if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        _icon.ToBitmap().Save(saveFileDialog.FileName, ImageFormat.Png);
                    }
                        #region Error handling
                    catch (Exception ex)
                    {
                        Msg.Inform(this, ex.Message, MsgSeverity.Warn);
                    }
                    #endregion
                }
            }
        }

        private void pageIcon_Commit(object sender, WizardPageConfirmEventArgs e)
        {
            _feedBuilder.Icons.Clear();
            try
            {
                if (textBoxHrefIco.Uri != null) _feedBuilder.Icons.Add(new Store.Model.Icon {Href = textBoxHrefIco.Uri, MimeType = Store.Model.Icon.MimeTypeIco});
                if (textBoxHrefPng.Uri != null) _feedBuilder.Icons.Add(new Store.Model.Icon {Href = textBoxHrefPng.Uri, MimeType = Store.Model.Icon.MimeTypePng});
            }
                #region Error handling
            catch (UriFormatException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
                return;
            }
            #endregion

            if (_feedBuilder.Icons.Count != 2)
                if (!Msg.YesNo(this, Resources.AskSkipIcon, MsgSeverity.Info)) e.Cancel = true;
        }
        #endregion

        #region pageSecurity
        /// <summary>Used to get a list of <see cref="OpenPgpSecretKey"/>s.</summary>
        private readonly IOpenPgp _openPgp;

        private void pageSecurity_Initialize(object sender, WizardPageInitEventArgs e)
        {
            ListKeys();
        }

        private void ListKeys()
        {
            comboBoxKeys.Items.Clear();
            comboBoxKeys.Items.Add("");
            comboBoxKeys.Items.AddRange(_openPgp.ListSecretKeys().Cast<object>().ToArray());

            comboBoxKeys.SelectedItem = _feedBuilder.SecretKey;
        }

        private void comboBoxKeys_SelectedIndexChanged(object sender, EventArgs e)
        {
            _feedBuilder.SecretKey = comboBoxKeys.SelectedItem as OpenPgpSecretKey;
        }

        private void buttonNewKey_Click(object sender, EventArgs e)
        {
            Process process;
            try
            {
                process = GnuPG.GenerateKey();
            }
                #region Error handling
            catch (IOException ex)
            {
                Log.Error(ex);
                Msg.Inform(this, ex.Message, MsgSeverity.Error);
                return;
            }
            #endregion

            ThreadUtils.StartBackground(() =>
            {
                process.WaitForExit();

                // Update key list when done
                try
                {
                    Invoke(new Action(ListKeys));
                }
                    #region Sanity checks
                catch (InvalidOperationException)
                {
                    // Ignore if window has been dispoed
                }
                #endregion
            }, name: "WaitForOpenPgp");
        }

        private void pageSecurity_Commit(object sender, WizardPageConfirmEventArgs e)
        {
            _feedBuilder.SecretKey = comboBoxKeys.SelectedItem as OpenPgpSecretKey;
            try
            {
                _feedBuilder.Uri = (textBoxInterfaceUri.Uri == null) ? null : new FeedUri(textBoxInterfaceUri.Uri);
            }
                #region Error handling
            catch (UriFormatException ex)
            {
                e.Cancel = true;
                Msg.Inform(this, ex.Message, MsgSeverity.Warn);
                return;
            }
            #endregion

            if (_feedBuilder.SecretKey == null || _feedBuilder.Uri == null)
                if (!Msg.YesNo(this, Resources.AskSkipSecurity, MsgSeverity.Info)) e.Cancel = true;
        }
        #endregion

        #region pageDone
        /// <summary>The result retrurned by <see cref="Run"/>.</summary>
        private SignedFeed _signedFeed;

        private void pageDone_Commit(object sender, WizardPageConfirmEventArgs e)
        {
            _signedFeed = _feedBuilder.Build();
        }
        #endregion
    }
}
