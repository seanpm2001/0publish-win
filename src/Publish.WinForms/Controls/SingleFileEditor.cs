// Copyright Bastian Eicher et al.
// Licensed under the GNU Lesser Public License

using System;
using NanoByte.Common;
using ZeroInstall.Publish.Properties;
using ZeroInstall.Store.Model;

namespace ZeroInstall.Publish.WinForms.Controls
{
    /// <summary>
    /// Edits <see cref="SingleFile"/> instances.
    /// </summary>
    public partial class SingleFileEditor : SingleFileEditorShim
    {
        public SingleFileEditor()
        {
            InitializeComponent();

            RegisterControl(textBoxDestination, new PropertyPointer<string>(() => Target.Destination, value => Target.Destination = value));
        }

        private void textBox_TextChanged(object sender, EventArgs e) => ShowUpdateHint(Resources.ManifestDigestChanged);
    }

    /// <summary>
    /// Non-generic base class for <see cref="SingleFileEditor"/>, because WinForms editor cannot handle generics.
    /// </summary>
    public class SingleFileEditorShim : DownloadRetrievalMethodEditor<SingleFile>
    {}
}
