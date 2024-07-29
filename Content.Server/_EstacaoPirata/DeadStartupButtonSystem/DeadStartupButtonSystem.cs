using Content.Server.Chat.Systems;
using Content.Server.Lightning;
using Content.Server.Popups;
using Content.Server.PowerCell;
using Content.Server.SimpleStation14.Silicon.Charge;
using Content.Shared._EstacaoPirata.DeadStartupButton;
using Content.Shared.Audio;
using Content.Shared.Damage;
using Content.Shared.Electrocution;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._EstacaoPirata.DeadStartupButtonSystem;

/// <summary>
/// This handles...
/// </summary>
public sealed class DeadStartupButtonSystem : SharedDeadStartupButtonSystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly LightningSystem _lightning = default!;
    [Dependency] private readonly SiliconChargeSystem _siliconChargeSystem = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;





    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<DeadStartupButtonComponent, OnDoAfterButtonPressedEvent>(OnDoAfter);
        SubscribeLocalEvent<DeadStartupButtonComponent, ElectrocutedEvent>(OnElectrocuted);
        SubscribeLocalEvent<DeadStartupButtonComponent, MobStateChangedEvent>(OnMobStateChanged);

    }

    private void OnDoAfter(EntityUid uid, DeadStartupButtonComponent comp, OnDoAfterButtonPressedEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (!TryComp(uid, out MobStateComponent? mobStateComponent) || !_mobState.IsDead(uid, mobStateComponent))
            return;
        if (!TryComp(uid, out MobThresholdsComponent? mobThresholdsComponent) || !TryComp(uid, out DamageableComponent? damageable))
            return;

        var criticalThreshold = _mobThreshold.GetThresholdForState(uid, MobState.Critical, mobThresholdsComponent);

        if (damageable.TotalDamage < criticalThreshold)
        {
            _mobState.ChangeMobState(uid, MobState.Alive, mobStateComponent);
        }
        else
        {
            _audio.PlayPvs(comp.BuzzSound, uid, AudioHelpers.WithVariation(0.05f, _robustRandom));
            _popup.PopupEntity(Loc.GetString("dead-startup-system-reboot-failed", ("target", MetaData(uid).EntityName)), uid);
            Spawn("EffectSparks", Transform(uid).Coordinates);
        }
    }

    //Haha funny easteregg
    private void OnElectrocuted(EntityUid uid, DeadStartupButtonComponent comp, ElectrocutedEvent args)
    {
        if (!TryComp(uid, out MobStateComponent? mobStateComponent) || !_mobState.IsDead(uid, mobStateComponent))
            return;

        if (!_siliconChargeSystem.TryGetSiliconBattery(uid, out var bateria) || bateria.CurrentCharge <= 0)
            return;

        _lightning.ShootRandomLightnings(uid, 2, 4);
        _powerCell.TryUseCharge(uid, bateria.CurrentCharge);

    }

    private void OnMobStateChanged(EntityUid uid, DeadStartupButtonComponent comp, MobStateChangedEvent args)
    {

        if (args.NewMobState == MobState.Alive)
        {
            _popup.PopupEntity(Loc.GetString("dead-startup-system-reboot-success", ("target", MetaData(uid).EntityName)), uid);
            _audio.PlayPvs(comp.Sound, uid);
        }

    }

}