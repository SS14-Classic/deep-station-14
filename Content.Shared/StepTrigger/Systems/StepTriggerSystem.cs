using Content.Shared.Gravity;
using Content.Shared.StepTrigger.Components;
using Content.Shared.Traits.Assorted.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Shared.StepTrigger.Systems;

public sealed class StepTriggerSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _entityLookup = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;

    public override void Initialize()
    {
        UpdatesOutsidePrediction = true;
        SubscribeLocalEvent<StepTriggerComponent, AfterAutoHandleStateEvent>(TriggerHandleState);

        SubscribeLocalEvent<StepTriggerComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<StepTriggerComponent, EndCollideEvent>(OnEndCollide);
#if DEBUG
        SubscribeLocalEvent<StepTriggerComponent, ComponentStartup>(OnStartup);
    }
    private void OnStartup(EntityUid uid, StepTriggerComponent component, ComponentStartup args)
    {
        if (!component.Active)
            return;

        if (!TryComp(uid, out FixturesComponent? fixtures) || fixtures.FixtureCount == 0)
            Log.Warning($"{ToPrettyString(uid)} has an active step trigger without any fixtures.");
#endif
    }

    public override void Update(float frameTime)
    {
        var query = GetEntityQuery<PhysicsComponent>();
        var enumerator = EntityQueryEnumerator<StepTriggerActiveComponent, StepTriggerComponent, TransformComponent>();

        while (enumerator.MoveNext(out var uid, out var active, out var trigger, out var transform))
        {
            if (!Update(uid, trigger, transform, query))
                continue;

            RemCompDeferred(uid, active);
        }
    }

    private bool Update(EntityUid uid, StepTriggerComponent component, TransformComponent transform, EntityQuery<PhysicsComponent> query)
    {
        if (!component.Active || component.Colliding.Count == 0)
            return true;

        if (component.Blacklist != null && TryComp<MapGridComponent>(transform.GridUid, out var grid))
        {
            var positon = _map.LocalToTile(transform.GridUid.Value, grid, transform.Coordinates);
            var anch = _map.GetAnchoredEntitiesEnumerator(uid, grid, positon);

            while (anch.MoveNext(out var ent))
            {
                if (ent == uid)
                    continue;

                if (_whitelistSystem.IsBlacklistPass(component.Blacklist, ent.Value))
                    return false;
            }
        }

        foreach (var otherUid in component.Colliding)
            UpdateColliding(uid, component, transform, otherUid, query);

        return false;
    }

    private void UpdateColliding(EntityUid uid, StepTriggerComponent component, TransformComponent ownerXform, EntityUid otherUid, EntityQuery<PhysicsComponent> query)
    {
        if (!query.TryGetComponent(otherUid, out var otherPhysics))
            return;

        var otherXform = Transform(otherUid);
        // TODO: This shouldn't be calculating based on world AABBs.
        var ourAabb = _entityLookup.GetAABBNoContainer(uid, ownerXform.LocalPosition, ownerXform.LocalRotation);
        var otherAabb = _entityLookup.GetAABBNoContainer(otherUid, otherXform.LocalPosition, otherXform.LocalRotation);

        if (!ourAabb.Intersects(otherAabb))
        {
            if (component.CurrentlySteppedOn.Remove(otherUid))
                Dirty(uid, component);

            return;
        }

        // max 'area of enclosure' between the two aabbs
        // this is hard to explain
        var intersect = Box2.Area(otherAabb.Intersect(ourAabb));
        var ratio = Math.Max(intersect / Box2.Area(otherAabb), intersect / Box2.Area(ourAabb));
        var requiredTriggeredSpeed = component.RequiredTriggeredSpeed;
        if (TryComp<TraitSpeedModifierComponent>(otherUid, out var speedModifier))
            requiredTriggeredSpeed *= speedModifier.RequiredTriggeredSpeedModifier;

        if (otherPhysics.LinearVelocity.Length() < requiredTriggeredSpeed
            || component.CurrentlySteppedOn.Contains(otherUid)
            || ratio < component.IntersectRatio
            || !CanTrigger(uid, otherUid, component))
            return;

        if (component.StepOn)
        {
            var evStep = new StepTriggeredOnEvent(uid, otherUid);
            RaiseLocalEvent(uid, ref evStep);
        }
        else
        {
            var evStep = new StepTriggeredOffEvent(uid, otherUid);
            RaiseLocalEvent(uid, ref evStep);
        }

        component.CurrentlySteppedOn.Add(otherUid);
        Dirty(uid, component);
    }

    private bool CanTrigger(EntityUid uid, EntityUid otherUid, StepTriggerComponent component)
    {
        if (!component.Active || component.CurrentlySteppedOn.Contains(otherUid))
            return false;

        // Can't trigger if we don't ignore weightless entities
        // and the entity is flying or currently weightless
        // Makes sense simulation wise to have this be part of steptrigger directly IMO
        if (!component.IgnoreWeightless && TryComp<PhysicsComponent>(otherUid, out var physics) &&
            (physics.BodyStatus == BodyStatus.InAir || _gravity.IsWeightless(otherUid, physics)))
            return false;

        var msg = new StepTriggerAttemptEvent { Source = (uid, component), Tripper = otherUid };
        RaiseLocalEvent(uid, ref msg);

        return msg.Continue && !msg.Cancelled;
    }

    private void OnStartCollide(EntityUid uid, StepTriggerComponent component, ref StartCollideEvent args)
    {
        var otherUid = args.OtherEntity;

        if (!args.OtherFixture.Hard
            || !CanTrigger(uid, otherUid, component))
            return;

        EnsureComp<StepTriggerActiveComponent>(uid);

        if (component.Colliding.Add(otherUid))
            Dirty(uid, component);
    }

    private void OnEndCollide(EntityUid uid, StepTriggerComponent component, ref EndCollideEvent args)
    {
        var otherUid = args.OtherEntity;

        if (!component.Colliding.Remove(otherUid))
            return;

        component.CurrentlySteppedOn.Remove(otherUid);
        Dirty(uid, component);

        if (component.StepOn)
        {
            var evStepOff = new StepTriggeredOffEvent(uid, otherUid);
            RaiseLocalEvent(uid, ref evStepOff);
        }

        if (component.Colliding.Count == 0)
            RemCompDeferred<StepTriggerActiveComponent>(uid);
    }

    private void TriggerHandleState(EntityUid uid, StepTriggerComponent component, ref AfterAutoHandleStateEvent args)
    {
        if (component.Colliding.Count > 0)
            EnsureComp<StepTriggerActiveComponent>(uid);
        else
            RemCompDeferred<StepTriggerActiveComponent>(uid);
    }

    public void SetIntersectRatio(EntityUid uid, float ratio, StepTriggerComponent? component = null)
    {
        if (!Resolve(uid, ref component)
            || MathHelper.CloseToPercent(component.IntersectRatio, ratio))
            return;

        component.IntersectRatio = ratio;
        Dirty(uid, component);
    }

    public void SetRequiredTriggerSpeed(EntityUid uid, float speed, StepTriggerComponent? component = null)
    {
        if (!Resolve(uid, ref component)
            || MathHelper.CloseToPercent(component.RequiredTriggeredSpeed, speed))
            return;

        component.RequiredTriggeredSpeed = speed;
        Dirty(uid, component);
    }

    public void SetActive(EntityUid uid, bool active, StepTriggerComponent? component = null)
    {
        if (!Resolve(uid, ref component)
            || active == component.Active)
            return;

        component.Active = active;
        Dirty(uid, component);
    }
}

/// <summary>
///     Raised at the beginning of a step trigger, and before entering the checks.
///     Allows for entities to end the steptrigger early via args.Cancelled.
/// </summary>
[ByRefEvent]
public record struct StepTriggerAttemptEvent(Entity<StepTriggerComponent> Source, EntityUid Tripper, bool Continue, bool Cancelled);

/// <summary>
///     Raised when an entity stands on a steptrigger initially (assuming it has both on and off states).
/// </summary>
[ByRefEvent]
public readonly record struct StepTriggeredOnEvent(EntityUid Source, EntityUid Tripper);

/// <summary>
///     Raised when an entity leaves a steptrigger if it has on and off states OR when an entity intersects a steptrigger.
/// </summary>
[ByRefEvent]
public readonly record struct StepTriggeredOffEvent(EntityUid Source, EntityUid Tripper);
