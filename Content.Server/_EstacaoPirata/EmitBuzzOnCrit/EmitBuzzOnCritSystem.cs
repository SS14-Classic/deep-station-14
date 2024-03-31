using Content.Server.Popups;
using Content.Shared._EstacaoPirata.EmitBuzzOnCrit;
using Content.Shared.Audio;
using Content.Shared.Body.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._EstacaoPirata.EmitBuzzOnCrit;

/// <summary>
/// This handles the buzzing popup and sound of a silicon based race when it goes into critical health.
/// </summary>
public sealed class EmitBuzzOnCritSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IRobustRandom _robustRandom = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<EmitBuzzOnCritComponent, BodyComponent>();

        while (query.MoveNext(out var uid, out var emitBuzzOnCritComponent, out var body))
        {
            if (_mobState.IsDead(uid))
                continue;
            if (!_mobState.IsCritical(uid))
                continue;

            emitBuzzOnCritComponent.AccumulatedFrametime += frameTime;

            if (emitBuzzOnCritComponent.AccumulatedFrametime < emitBuzzOnCritComponent.CycleDelay)
                continue;
            emitBuzzOnCritComponent.AccumulatedFrametime -= emitBuzzOnCritComponent.CycleDelay;


            // start buzzing
            if (_gameTiming.CurTime >= emitBuzzOnCritComponent.LastBuzzPopupTime + emitBuzzOnCritComponent.BuzzPopupCooldown)
            {
                emitBuzzOnCritComponent.LastBuzzPopupTime = _gameTiming.CurTime;
                _popupSystem.PopupEntity(Loc.GetString("silicon-behavior-buzz"), uid);
                _audio.PlayPvs(emitBuzzOnCritComponent.Sound, uid, AudioHelpers.WithVariation(0.05f, _robustRandom));
            }
        }
    }

}
