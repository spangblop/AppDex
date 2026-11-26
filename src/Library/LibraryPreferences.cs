using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GameShelf
{
    [DataContract]
    internal sealed class AppGroup
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public bool Open { get; set; } = true;
        [DataMember] public List<string> Apps { get; set; } = new List<string>();
    }

    [DataContract]
    internal sealed class LibraryPreferences
    {
        [DataMember] public int Sort;
        [DataMember] public int Source;
        [DataMember(EmitDefaultValue = false)] public int Style;
        [DataMember] public AppearanceOptions Appearance = AppAppearance.Defaults();
        [DataMember(EmitDefaultValue = false)] public List<AppearancePreset> Presets = new List<AppearancePreset>();
        [DataMember] public double Scale = 1;
        [DataMember] public string Accent = AppAppearance.DefaultAccent;
        [DataMember(EmitDefaultValue = false)] public bool ConfirmLaunch;
        [DataMember(EmitDefaultValue = false)] public bool QuitAfterLaunch;
        [DataMember(EmitDefaultValue = false)] public bool MinimizeAfterLaunch;
        [DataMember(EmitDefaultValue = false)] public bool QuitOnWindowClose;
        [DataMember(EmitDefaultValue = false)] public bool SkipQuitConfirmation;
        [DataMember(EmitDefaultValue = false)] public bool StartupInTray;
        [DataMember(EmitDefaultValue = false)] public bool UiSoundsEnabled;
        [DataMember(EmitDefaultValue = false)] public int? QuickLaunchModifiers;
        [DataMember(EmitDefaultValue = false)] public int? QuickLaunchKey;
        [DataMember(EmitDefaultValue = false)] public bool SyncSteam;
        [DataMember(EmitDefaultValue = false)] public bool SyncEpic;
        [DataMember] public List<string> Order = new List<string>();
        [DataMember] public List<string> Pins = new List<string>();
        [DataMember] public List<AppGroup> Groups = new List<AppGroup>();

        private static DataContractJsonSerializer Serializer() { return new DataContractJsonSerializer(typeof(LibraryPreferences)); }

        internal static LibraryPreferences Load(string file)
        {
            try
            {
                using (var input = File.OpenRead(file))
                {
                    var prefs = (LibraryPreferences)Serializer().ReadObject(input);
                    if (prefs != null && prefs.Order != null && prefs.Pins != null && prefs.Groups != null)
                    {
                        prefs.Sort = Math.Max(0, Math.Min(2, prefs.Sort));
                        prefs.Source = Math.Max(0, Math.Min(3, prefs.Source));
                        if (prefs.Appearance == null) prefs.Appearance = AppAppearance.Defaults(prefs.Style);
                        prefs.Appearance.Normalize();
                        prefs.Style = 0;
                        if (!(prefs.Scale >= 0.75 && prefs.Scale <= 1.5)) prefs.Scale = 1;
                        prefs.Accent = AppAppearance.Hex(AppAppearance.ReadColor(prefs.Accent));
                        prefs.NormalizePresets();
                        return prefs;
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is SerializationException || e is UnauthorizedAccessException) { }
            return new LibraryPreferences();
        }

        internal void Save(string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temp = file + ".tmp";
            using (var output = File.Create(temp))
            {
                Serializer().WriteObject(output, this);
                output.Flush(true);
            }
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
        }

        private void NormalizePresets()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Presets = (Presets ?? new List<AppearancePreset>()).Where(preset =>
                preset != null && preset.Appearance != null && !string.IsNullOrWhiteSpace(preset.Name) &&
                preset.Name.Trim().Length <= AppearancePreset.NameLimit && names.Add(preset.Name.Trim())).ToList();
            foreach (var preset in Presets) preset.Normalize();
        }

        internal void Sync(IEnumerable<string> ids)
        {
            var remaining = new HashSet<string>(ids);
            var claimed = new HashSet<string>();
            Groups = Groups.Where(g => g != null && g.Id != null && g.Apps != null && Order.Contains(g.Id)).ToList();
            foreach (var g in Groups)
                g.Apps = g.Apps.Where(id => remaining.Contains(id) && claimed.Add(id)).ToList();
            DropEmptyGroups();
            var roots = new HashSet<string>(remaining.Except(claimed).Concat(Groups.Select(g => g.Id)));
            Order = Order.Where(roots.Remove).Concat(ids.Where(roots.Remove)).ToList();
            Pins = Pins.Where(remaining.Contains).Distinct().ToList();
        }

        internal AppGroup GroupOf(string id) { return Groups.FirstOrDefault(g => g.Apps.Contains(id)); }

        internal LibraryPreferences Copy()
        {
            var preview = (LibraryPreferences)MemberwiseClone();
            preview.Appearance = Appearance.Copy();
            preview.Presets = Presets.Select(preset => preset.Copy()).ToList();
            preview.Order = new List<string>(Order);
            preview.Pins = new List<string>(Pins);
            preview.Groups = Groups.Select(g => new AppGroup {
                Id = g.Id, Name = g.Name, Open = g.Open, Apps = new List<string>(g.Apps) }).ToList();
            return preview;
        }

        private void DropEmptyGroups()
        {
            foreach (var g in Groups.Where(g => g.Apps.Count == 0).ToArray())
            {
                Groups.Remove(g);
                Order.Remove(g.Id);
            }
        }

        internal void Move(string id, string target, bool into, bool before)
        {
            if (id == target) return;
            var sourceGroup = GroupOf(id);
            var destination = Groups.FirstOrDefault(g => g.Id == target) ?? GroupOf(target);
            var movingGroup = Groups.FirstOrDefault(g => g.Id == id);
            if (movingGroup != null)
            {
                if (destination == movingGroup) return;
                Insert(Order, id, destination == null ? target : destination.Id, before);
                return;
            }

            if (sourceGroup != null) sourceGroup.Apps.Remove(id);
            else Order.Remove(id);
            if (destination != null && (target != destination.Id || into))
            {
                if (target == destination.Id) destination.Apps.Add(id);
                else Insert(destination.Apps, id, target, before);
                destination.Open = true;
            }
            else if (destination == null && into && target != null && Order.Contains(target))
            {
                var group = new AppGroup { Id = "group-" + Guid.NewGuid().ToString("N"), Name = "New group" };
                group.Apps.Add(target);
                group.Apps.Add(id);
                Order[Order.IndexOf(target)] = group.Id;
                Groups.Add(group);
            }
            else Insert(Order, id, target, before);
            DropEmptyGroups();
        }

        internal void Ungroup(string id)
        {
            var group = GroupOf(id);
            if (group == null) return;
            group.Apps.Remove(id);
            Insert(Order, id, group.Id, false);
            DropEmptyGroups();
        }

        internal void Dissolve(AppGroup group)
        {
            int index = Order.IndexOf(group.Id);
            if (index >= 0) { Order.RemoveAt(index); Order.InsertRange(index, group.Apps); }
            Groups.Remove(group);
        }

        private static void Insert(List<string> list, string id, string target, bool before)
        {
            list.Remove(id);
            int index = target == null ? -1 : list.IndexOf(target);
            list.Insert(index < 0 ? list.Count : index + (before ? 0 : 1), id);
        }
    }
}
