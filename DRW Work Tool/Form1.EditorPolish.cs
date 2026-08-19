using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Linq;
using DRW_Work_Tool.Core;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private Button? _cloneTemplateButton;
        private TabPage? _cloneTemplateHost;
        private Control? _cloneTemplateLayoutHost;
        private bool _editorPolishInitialized;

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            InitializeEditorPolish();
        }

        private void InitializeEditorPolish()
        {
            if (_editorPolishInitialized || editorTabs == null)
                return;

            _editorPolishInitialized = true;

            editorTabs.SelectedIndexChanged += (_, _) => RefreshEditorPolish();
            editorTabs.ControlAdded += (_, _) => BeginInvoke(new Action(RefreshEditorPolish));
            editorTabs.ControlRemoved += (_, _) => BeginInvoke(new Action(RefreshEditorPolish));

            RefreshEditorPolish();
        }

        private void RefreshEditorPolish()
        {
            if (editorTabs == null || editorTabs.IsDisposed)
                return;

            TabPage? page = editorTabs.SelectedTab;
            if (page == null)
            {
                RemoveCloneTemplateButton();
                return;
            }

            ApplyEditorPerformancePolish(page);
            FixKnownEditorLayouts(page);

            if (!TryGetCloneContext(page.Tag, out _, out _))
            {
                RemoveCloneTemplateButton();
                return;
            }

            EnsureCloneTemplateButton(page);
        }

        private void EnsureCloneTemplateButton(TabPage page)
        {
            Control layoutHost = FindCloneTemplateLayoutHost(page) ?? page;

            if (!ReferenceEquals(_cloneTemplateHost, page) ||
                !ReferenceEquals(_cloneTemplateLayoutHost, layoutHost) ||
                _cloneTemplateButton == null ||
                _cloneTemplateButton.IsDisposed)
            {
                RemoveCloneTemplateButton();

                _cloneTemplateHost = page;
                _cloneTemplateLayoutHost = layoutHost;
                _cloneTemplateButton = CreateEditorActionButton("CLONE TEMPLATE");
                _cloneTemplateButton.Name = "GlobalCloneTemplateButton";
                _cloneTemplateButton.Size = new Size(158, 34);
                _cloneTemplateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                _cloneTemplateButton.Click += (_, _) => CloneSelectedEditorTemplate();

                editorToolTip.SetToolTip(
                    _cloneTemplateButton,
                    "Clone the current record, assign a fresh ID when supported, and open the clone as a new editable record.");

                layoutHost.Controls.Add(_cloneTemplateButton);
                layoutHost.Resize += CloneTemplateHost_Resize;
            }

            PositionCloneTemplateButton();
            _cloneTemplateButton.BringToFront();
        }

        private static Control? FindCloneTemplateLayoutHost(Control root)
        {
            List<Panel> panels = EnumerateControls(root)
                .OfType<Panel>()
                .Where(panel =>
                    panel.Dock == DockStyle.Top &&
                    panel.Height >= 48 &&
                    panel.Height <= 110)
                .ToList();

            Panel? withEditorActions = panels
                .FirstOrDefault(panel =>
                    panel.Controls
                        .OfType<Button>()
                        .Any(button =>
                            button.Text.Equals("SAVE", StringComparison.OrdinalIgnoreCase) ||
                            button.Text.Contains("XML", StringComparison.OrdinalIgnoreCase)));

            return withEditorActions ?? panels.FirstOrDefault();
        }

        private void CloneTemplateHost_Resize(object? sender, EventArgs e)
        {
            PositionCloneTemplateButton();
        }

        private void PositionCloneTemplateButton()
        {
            if (_cloneTemplateHost == null ||
                _cloneTemplateLayoutHost == null ||
                _cloneTemplateButton == null ||
                _cloneTemplateButton.IsDisposed)
            {
                return;
            }

            int x = Math.Max(
                12,
                _cloneTemplateLayoutHost.ClientSize.Width -
                _cloneTemplateButton.Width - 16);

            int y = Math.Max(
                8,
                (_cloneTemplateLayoutHost.ClientSize.Height -
                 _cloneTemplateButton.Height) / 2);

            _cloneTemplateButton.Location = new Point(x, y);
        }

        private void RemoveCloneTemplateButton()
        {
            if (_cloneTemplateLayoutHost != null)
                _cloneTemplateLayoutHost.Resize -= CloneTemplateHost_Resize;

            if (_cloneTemplateButton != null)
            {
                if (!_cloneTemplateButton.IsDisposed && _cloneTemplateButton.Parent != null)
                    _cloneTemplateButton.Parent.Controls.Remove(_cloneTemplateButton);

                _cloneTemplateButton.Dispose();
            }

            _cloneTemplateButton = null;
            _cloneTemplateHost = null;
            _cloneTemplateLayoutHost = null;
        }

        private void CloneSelectedEditorTemplate()
        {
            TabPage? page = editorTabs?.SelectedTab;

            if (page == null ||
                !TryGetCloneContext(page.Tag, out XElement working, out object service))
            {
                return;
            }

            XElement clone = new XElement(working);

            TryAssignFreshCloneId(service, clone);
            TryMarkCloneName(clone);

            if (!TryOpenCloneEditor(service, clone))
            {
                MessageBox.Show(
                    "This editor does not expose a compatible clone entry point yet.\r\n\r\n" +
                    "The source record was not changed.",
                    "Clone Template",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private static bool TryGetCloneContext(
            object? state,
            out XElement working,
            out object service)
        {
            working = null!;
            service = null!;

            if (state == null)
                return false;

            object? workingValue = ReadStateMember(state, "Working");
            object? serviceValue = ReadStateMember(state, "Service");

            if (workingValue is not XElement element || serviceValue == null)
                return false;

            working = element;
            service = serviceValue;
            return true;
        }

        private static object? ReadStateMember(object state, string name)
        {
            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            Type type = state.GetType();

            FieldInfo? field = type.GetField(name, flags);
            if (field != null)
                return field.GetValue(state);

            PropertyInfo? property = type.GetProperty(name, flags);
            return property?.GetValue(state);
        }

        private static readonly string[] CloneIdentityElementNames =
        {
            "s_dwID",
            "s_nID",
            "dwID",
            "nID",
            "ID",
            "Id"
        };

        private static void TryAssignFreshCloneId(object service, XElement clone)
        {
            XElement? idElement = CloneIdentityElementNames
                .Select(name => clone.Element(name))
                .FirstOrDefault(x => x != null);

            if (idElement == null ||
                !uint.TryParse(
                    idElement.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint currentId))
            {
                return;
            }

            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            MethodInfo? method = service.GetType()
                .GetMethods(flags)
                .Where(x => x.Name.Equals("SuggestAvailableId", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(x => x.GetParameters().Length == 1);

            if (method == null)
                return;

            Type parameterType = method.GetParameters()[0].ParameterType;
            object? input = ConvertIntegerForParameter(
                currentId == uint.MaxValue ? currentId : currentId + 1,
                parameterType);

            if (input == null)
                return;

            try
            {
                object? result = method.Invoke(service, new[] { input });
                if (result != null)
                {
                    idElement.Value =
                        Convert.ToString(result, CultureInfo.InvariantCulture)
                        ?? idElement.Value;
                }
            }
            catch
            {
                // The target editor still protects duplicate IDs during save.
            }
        }

        private static object? ConvertIntegerForParameter(uint value, Type parameterType)
        {
            Type target = Nullable.GetUnderlyingType(parameterType) ?? parameterType;

            try
            {
                if (target == typeof(uint)) return value;
                if (target == typeof(int)) return checked((int)value);
                if (target == typeof(long)) return (long)value;
                if (target == typeof(ulong)) return (ulong)value;
                if (target == typeof(short)) return checked((short)value);
                if (target == typeof(ushort)) return checked((ushort)value);
            }
            catch (OverflowException)
            {
                return null;
            }

            return null;
        }

        private static void TryMarkCloneName(XElement clone)
        {
            XElement? name =
                clone.Element("s_szName") ??
                clone.Element("Name") ??
                clone.Element("name");

            if (name == null ||
                string.IsNullOrWhiteSpace(name.Value) ||
                name.Value.EndsWith(" [Clone]", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            name.Value += " [Clone]";
        }

        private bool TryOpenCloneEditor(object service, XElement clone)
        {
            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.NonPublic;

            string serviceHint = service.GetType().Name
                .Replace("EditorService", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("Service", string.Empty, StringComparison.OrdinalIgnoreCase);

            IEnumerable<MethodInfo> candidates = GetType()
                .GetMethods(flags)
                .Where(method =>
                    method.Name.StartsWith("Open", StringComparison.Ordinal) &&
                    method.Name.Contains("Edit", StringComparison.OrdinalIgnoreCase))
                .Where(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Any(p => p.ParameterType.IsInstanceOfType(service)) &&
                           parameters.Any(p => p.ParameterType == typeof(XElement)) &&
                           parameters.Any(p => p.ParameterType == typeof(bool));
                })
                .OrderByDescending(method =>
                    method.Name.Contains(serviceHint, StringComparison.OrdinalIgnoreCase))
                .ThenBy(method => method.GetParameters().Length);

            foreach (MethodInfo method in candidates)
            {
                if (!TryBuildCloneArguments(method, service, clone, out object?[] arguments))
                    continue;

                try
                {
                    method.Invoke(this, arguments);
                    return true;
                }
                catch (TargetInvocationException ex)
                {
                    Exception actual = ex.InnerException ?? ex;
                    AppLogger.Warning("Clone Template: " + actual.Message);
                }
                catch (Exception ex)
                {
                    AppLogger.Warning("Clone Template: " + ex.Message);
                }
            }

            return false;
        }

        private static bool TryBuildCloneArguments(
            MethodInfo method,
            object service,
            XElement clone,
            out object?[] arguments)
        {
            ParameterInfo[] parameters = method.GetParameters();
            arguments = new object?[parameters.Length];

            bool cloneAssigned = false;
            bool serviceAssigned = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];

                if (!serviceAssigned && parameter.ParameterType.IsInstanceOfType(service))
                {
                    arguments[i] = service;
                    serviceAssigned = true;
                    continue;
                }

                if (parameter.ParameterType == typeof(XElement))
                {
                    if (!cloneAssigned)
                    {
                        arguments[i] = clone;
                        cloneAssigned = true;
                    }
                    else
                    {
                        arguments[i] = null;
                    }

                    continue;
                }

                if (parameter.ParameterType == typeof(bool))
                {
                    arguments[i] = true;
                    continue;
                }

                if (parameter.HasDefaultValue)
                {
                    arguments[i] = parameter.DefaultValue;
                    continue;
                }

                Type? nullable = Nullable.GetUnderlyingType(parameter.ParameterType);
                if (!parameter.ParameterType.IsValueType || nullable != null)
                {
                    arguments[i] = null;
                    continue;
                }

                return false;
            }

            return cloneAssigned && serviceAssigned;
        }

        private static void FixKnownEditorLayouts(TabPage page)
        {
            string stateName = page.Tag?.GetType().Name ?? string.Empty;

            if (stateName.Contains("BuffEditState", StringComparison.Ordinal))
                FixBuffIdentityLayout(page);
        }

        private static void FixBuffIdentityLayout(Control root)
        {
            Panel? identity = FindSection(root, "IDENTITY / TEXT");
            if (identity == null)
                return;

            identity.Height = Math.Max(identity.Height, 286);

            Label? effectLabel = identity.Controls
                .OfType<Label>()
                .FirstOrDefault(x =>
                    x.Text.Equals("Effect File", StringComparison.OrdinalIgnoreCase));

            TextBox? effectBox = identity.Controls
                .OfType<TextBox>()
                .FirstOrDefault(x =>
                    string.Equals(
                        x.Tag as string,
                        "s_szEffectFile",
                        StringComparison.Ordinal));

            if (effectLabel != null)
                effectLabel.Top = 214;

            if (effectBox != null)
                effectBox.Top = 236;

            Panel? relations = FindSection(root, "SKILL RELATIONS");
            Panel? behavior = FindSection(root, "BUFF BEHAVIOR");

            if (relations != null)
                relations.Top = Math.Max(relations.Top, 452);

            if (behavior != null)
                behavior.Top = Math.Max(behavior.Top, 684);

            Control? content = identity.Parent;
            if (content != null)
                content.Height = Math.Max(content.Height, 1150);
        }

        private static Panel? FindSection(Control root, string title)
        {
            foreach (Control control in EnumerateControls(root))
            {
                if (control is not Panel panel)
                    continue;

                if (panel.Controls.OfType<Label>().Any(label =>
                        label.Text.Equals(title, StringComparison.OrdinalIgnoreCase)))
                {
                    return panel;
                }
            }

            return null;
        }

        private static void ApplyEditorPerformancePolish(Control root)
        {
            foreach (Control control in EnumerateControls(root))
            {
                if (control is FlowLayoutPanel ||
                    control is Panel ||
                    control is TableLayoutPanel)
                {
                    TryEnableDoubleBuffering(control);
                }
            }
        }

        private static IEnumerable<Control> EnumerateControls(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;

                foreach (Control nested in EnumerateControls(child))
                    yield return nested;
            }
        }

        private static void TryEnableDoubleBuffering(Control control)
        {
            try
            {
                PropertyInfo? property = typeof(Control).GetProperty(
                    "DoubleBuffered",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                property?.SetValue(control, true);
            }
            catch
            {
                // Visual optimization only; never block editor usage.
            }
        }
    }
}
