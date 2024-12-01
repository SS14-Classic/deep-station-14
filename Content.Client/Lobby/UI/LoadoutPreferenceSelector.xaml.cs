using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client.Guidebook;
using Content.Client.Paint;
using Content.Client.Players.PlayTimeTracking;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Shared.Clothing.Loadouts.Prototypes;
using Content.Shared.Clothing.Loadouts.Systems;
using Content.Shared.Customization.Systems;
using Content.Shared.Paint;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;


[GenerateTypedNameReferences]
public sealed partial class LoadoutPreferenceSelector : Control
{
    public const string DefaultLoadoutInfoGuidebook = "LoadoutInfo";

    public EntityUid DummyEntityUid;
    private readonly IEntityManager _entityManager;

    public LoadoutPrototype Loadout { get; }

    private LoadoutPreference _preference = null!;
    public LoadoutPreference Preference
    {
        get => _preference;
        set
        {
            _preference = value;
            NameEdit.Text = value.CustomName ?? "";
            DescriptionEdit.TextRope = new Rope.Leaf(value.CustomDescription ?? "");
            ColorEdit.Color = Color.FromHex(value.CustomColorTint, Color.White);
            if (value.CustomColorTint != null)
                UpdatePaint(new(DummyEntityUid, _entityManager.GetComponent<PaintedComponent>(DummyEntityUid)), _entityManager);
            HeirloomButton.Pressed = value.CustomHeirloom ?? false;
            PreferenceButton.Pressed = value.Selected;
        }
    }

    public bool Valid;
    private bool _showUnusable;
    public bool ShowUnusable
    {
        get => _showUnusable;
        set
        {
            _showUnusable = value;
            Visible = Valid && _wearable || _showUnusable;
            PreferenceButton.RemoveStyleClass(StyleBase.ButtonDanger);
            PreferenceButton.AddStyleClass(Valid ? "" : StyleBase.ButtonDanger);
        }
    }

    private bool _wearable;
    public bool Wearable
    {
        get => _wearable;
        set
        {
            _wearable = value;
            Visible = Valid && _wearable || _showUnusable;
            PreferenceButton.RemoveStyleClass(StyleBase.ButtonCaution);
            PreferenceButton.AddStyleClass(_wearable ? "" : StyleBase.ButtonCaution);
        }
    }

    public event Action<LoadoutPreference>? PreferenceChanged;


    public LoadoutPreferenceSelector(LoadoutPrototype loadout, JobPrototype highJob,
        HumanoidCharacterProfile profile, ref Dictionary<string, EntityUid> entities,
        IEntityManager entityManager, IPrototypeManager prototypeManager, IConfigurationManager configManager,
        CharacterRequirementsSystem characterRequirementsSystem, JobRequirementsManager jobRequirementsManager)
    {
        RobustXamlLoader.Load(this);

        _entityManager = entityManager;
        Loadout = loadout;

        // Show/hide the special menu and items depending on what's allowed
        HeirloomButton.Visible = loadout.CanBeHeirloom;
        SpecialMenu.Visible = Loadout.CustomName || Loadout.CustomDescription || Loadout.CustomColorTint;
        SpecialName.Visible = Loadout.CustomName;
        SpecialDescription.Visible = Loadout.CustomDescription;
        SpecialColorTintToggle.Visible = Loadout.CustomColorTint;


        SpriteView previewLoadout;
        if (!entities.TryGetValue(loadout.ID + 0, out var dummyLoadoutItem))
        {
            // Get the first item in the loadout to be the preview
            dummyLoadoutItem = entityManager.SpawnEntity(loadout.Items.First(), MapCoordinates.Nullspace);
            entities.Add(loadout.ID + 0, dummyLoadoutItem);

            // Create a sprite preview of the loadout item
            previewLoadout = new SpriteView
            {
                Scale = new Vector2(1, 1),
                OverrideDirection = Direction.South,
                VerticalAlignment = VAlignment.Center,
                SizeFlagsStretchRatio = 1,
            };
            previewLoadout.SetEntity(dummyLoadoutItem);
        }
        else
        {
            // Create a sprite preview of the loadout item
            previewLoadout = new SpriteView
            {
                Scale = new Vector2(1, 1),
                OverrideDirection = Direction.South,
                VerticalAlignment = VAlignment.Center,
                SizeFlagsStretchRatio = 1,
            };
            previewLoadout.SetEntity(dummyLoadoutItem);
        }
        DummyEntityUid = dummyLoadoutItem;

        entityManager.EnsureComponent<AppearanceComponent>(dummyLoadoutItem);
        entityManager.EnsureComponent<PaintedComponent>(dummyLoadoutItem, out var paint);

        var loadoutName =
            Loc.GetString($"loadout-name-{loadout.ID}") == $"loadout-name-{loadout.ID}"
                ? entityManager.GetComponent<MetaDataComponent>(dummyLoadoutItem).EntityName
                : Loc.GetString($"loadout-name-{loadout.ID}");
        var loadoutDesc =
            !Loc.TryGetString($"loadout-description-{loadout.ID}", out var description)
                ? entityManager.GetComponent<MetaDataComponent>(dummyLoadoutItem).EntityDescription
                : description;


        // Manage the info button
        void UpdateGuidebook() => GuidebookButton.Visible =
            prototypeManager.HasIndex<GuideEntryPrototype>(DefaultLoadoutInfoGuidebook + Loadout.ID);
        UpdateGuidebook();
        prototypeManager.PrototypesReloaded += _ => UpdateGuidebook();

        GuidebookButton.OnPressed += _ =>
        {
            if (!prototypeManager.TryIndex<GuideEntryPrototype>(DefaultLoadoutInfoGuidebook, out var guideRoot))
                return;

            var guidebookController = UserInterfaceManager.GetUIController<GuidebookUIController>();
            //TODO: Don't close the guidebook if its already open, just go to the correct page
            guidebookController.ToggleGuidebook(
                new Dictionary<string, GuideEntry> { { DefaultLoadoutInfoGuidebook, guideRoot } },
                includeChildren: true,
                selected: DefaultLoadoutInfoGuidebook + Loadout.ID);
        };

        // Create a checkbox to get the loadout
        PreferenceButton.AddChild(new BoxContainer
        {
            Children =
            {
                new Label
                {
                    Text = loadout.Cost.ToString(),
                    StyleClasses = { StyleBase.StyleClassLabelHeading },
                    MinWidth = 32,
                    MaxWidth = 32,
                    ClipText = true,
                    Margin = new Thickness(0, 0, 8, 0),
                },
                new PanelContainer
                {
                    PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#2f2f2f") },
                    Children =
                    {
                        previewLoadout,
                    },
                },
                new Label
                {
                    Text = loadoutName,
                    Margin = new Thickness(8, 0, 0, 0),
                },
            },
        });
        PreferenceButton.OnToggled += args =>
        {
            _preference.Selected = args.Pressed;
            PreferenceChanged?.Invoke(Preference);
        };
        HeirloomButton.OnToggled += args =>
        {
            _preference.CustomHeirloom = args.Pressed ? true : null;
            PreferenceChanged?.Invoke(Preference);
        };
        SaveButton.OnPressed += _ =>
        {
            _preference.CustomColorTint = SpecialColorTintToggle.Pressed ? ColorEdit.Color.ToHex() : null;
            _preference.Selected = PreferenceButton.Pressed;
            PreferenceChanged?.Invoke(Preference);
        };

        // Update prefs cache when something changes
        NameEdit.OnTextChanged += _ =>
            _preference.CustomName = string.IsNullOrEmpty(NameEdit.Text) ? null : NameEdit.Text;
        DescriptionEdit.OnTextChanged += _ =>
            _preference.CustomDescription = string.IsNullOrEmpty(Rope.Collapse(DescriptionEdit.TextRope)) ? null : Rope.Collapse(DescriptionEdit.TextRope);
        SpecialColorTintToggle.OnToggled += args =>
            ColorEdit.Visible = args.Pressed;
        ColorEdit.OnColorChanged += _ =>
        {
            _preference.CustomColorTint = SpecialColorTintToggle.Pressed ? ColorEdit.Color.ToHex() : null;
            UpdatePaint(new Entity<PaintedComponent>(dummyLoadoutItem, paint), entityManager);
        };

        NameEdit.PlaceHolder = loadoutName;
        DescriptionEdit.Placeholder = new Rope.Leaf(Loc.GetString(loadoutDesc));


        var tooltip = new StringBuilder();
        // Add the loadout description to the tooltip if there is one
        if (!string.IsNullOrEmpty(loadoutDesc))
            tooltip.Append($"{Loc.GetString(loadoutDesc)}");

        // Get requirement reasons
        characterRequirementsSystem.CheckRequirementsValid(
            loadout.Requirements, highJob, profile, new Dictionary<string, TimeSpan>(),
            jobRequirementsManager.IsWhitelisted(), loadout,
            entityManager, prototypeManager, configManager,
            out var reasons);

        // Add requirement reasons to the tooltip
        foreach (var reason in reasons)
            tooltip.Append($"\n{reason}");

        // Combine the tooltip and format it in the checkbox supplier
        if (tooltip.Length > 0)
        {
            var formattedTooltip = new Tooltip();
            formattedTooltip.SetMessage(FormattedMessage.FromMarkupPermissive(tooltip.ToString()));
            PreferenceButton.TooltipSupplier = _ => formattedTooltip;
        }
    }

    private bool _initialized;
    protected override void FrameUpdate(FrameEventArgs args)
    {
        if (_initialized || SpecialMenu.Heading == null)
            return;

        // Move the special editor
        var heading = SpecialMenu.Heading;
        heading.Orphan();
        ButtonGroup.AddChild(heading);
        GuidebookButton.Orphan();
        ButtonGroup.AddChild(GuidebookButton);

        // These guys are here too for reasons
        HeadingButton.SetHeight = HeirloomButton.SetHeight = GuidebookButton.SetHeight = PreferenceButton.Size.Y;
        SpecialColorTintToggle.Pressed = ColorEdit.Visible = _preference.CustomColorTint != null;

        _initialized = true;
    }


    private void UpdatePaint(Entity<PaintedComponent> entity, IEntityManager entityManager)
    {
        if (_preference.CustomColorTint != null)
        {
            entity.Comp.Color = Color.FromHex(_preference.CustomColorTint);
            entity.Comp.Enabled = true;
        }
        else
            entity.Comp.Enabled = false;

        var app = entityManager.System<SharedAppearanceSystem>();
        app.TryGetData(entity, PaintVisuals.Painted, out bool value);
        app.SetData(entity, PaintVisuals.Painted, !value);
    }
}