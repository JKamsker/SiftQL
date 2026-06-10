# Feature Request: Polymorphic type discrimination in filter predicates (`is` / `as`)

> **Implementation status (shipped on `feat/filter-type-discrimination`).** `is`
> and downcast-`as` now translate. The shipped design **deliberately diverges**
> from the tiered proposal below — it favours zero-config, open subtype matching
> over the registered, leaf-name model. The proposal is kept as the record of the
> alternative that was considered.
>
> **What shipped**
> - `x is T` / `x.Member is T` translate to `Contains("<path>.subjectTypes", typeof(T).FullName)`.
>   `subjectTypes` is a synthetic reserved array field carrying the runtime
>   value's full type ancestry (its type, base types, and interfaces), injected at
>   `FilterSchema` construction for both generated and reflection-fallback schemas.
> - `(x.Member as Sub).Prop` projects subtype-specific members under a
>   `Member.<Sub>.Prop` path (Tier 3); base-declared members stay flat. Scalar and
>   array members are projected; array members compose with `Contains`.
> - Tests: `tests/Generators.Tests/Runtime/Filters/KernelTypeTestTranslationTests.cs`.
>
> **How it diverges from the proposal below**
> 1. **Semantics — true subtype + interface match, not leaf-name.** `is` matches
>    the target type and *every subtype/interface implementation* (`orc is Monster`
>    is true), via an ancestry `Contains`. The proposal lowers to
>    `Compare("subjectName", Equal, id)`, which matches only the exact leaf type.
> 2. **No registration / no stable ids.** There is no `[SiftPolymorphic]` /
>    `RegisterPolymorphic`. Root and interface `is` need no registration; nested
>    `is` and Tier-3 `as` reuse the existing `FilterSchema.RegisterValueObject`
>    set. The discriminator wire value is `Type.FullName`, so a CLR rename breaks a
>    previously-serialized filter (the proposal's stable ids solve this; not done).
> 3. **`is` never throws now.** Any type test translates (the proposal keeps
>    unregistered type tests throwing).
> 4. **Lowered form is `Contains`, not `Compare`.** It serializes through
>    `FilterDocument` (JSON) but **does not round-trip through the text DSL**
>    (`FilterQuery` has no `Contains` syntax). The proposal's `Compare` form would.
> 5. **Tier-3 scope.** The subtype segment is `<Sub.Name>` (simple name; same
>    simple-name subtypes in different namespaces would collide). Only scalar and
>    array subtype members are projected — nested-object subtype members are not.
>
> **Not done (candidates for follow-up):** stable discriminator ids + attribute
> registration + generator; text-DSL `Contains`/`is` round-trip; nested-object
> subtype projection; Tier-3 for generated subjects without value-object
> registration.

## Summary

SiftQL filter predicates cannot express type discrimination over a polymorphic
member. Today this throws at translation time:

```csharp
// x.Defender is a base/interface type with concrete subtypes (Player, Monster):
QueryKernel.For<CombatResult>()
    .Where(static ev => ev.Defender is Monster);                       // throws

QueryKernel.For<CombatResult>()
    .Where(static ev => (ev.Attacker as Player)!.EquippedItemIds       // throws
        .Contains(itemId));
```

Both forms fail in the translator: `is` is an `ExpressionType.TypeIs` node with no
case in `KernelExpressionTranslator.Translate`, and a *downcast* `as` is rejected
by `ValidateFieldConversion`. There is no escape hatch — a consumer that wants this
authoring shape must run its own `ExpressionVisitor` over every predicate to
rewrite the type operations into supported nodes *before* handing them to SiftQL.

This is avoidable. SiftQL already does runtime-type discrimination for the **root
subject**: `FilterSchemaFallbackBuilder` emits two virtual scalar fields,
`subjectType` and `subjectName`, and for a non-sealed subject they are computed
from the *runtime instance's* actual type, not the declared type. And
`FilterNullCheck` already lowers an operator over a structurally-typed (non-scalar)
field into a presence primitive (`Exists` / `Not(Exists)`).

This request proposes generalizing those two existing mechanisms so that:

- `x is Sub` and `x is not Sub` translate to a discriminator comparison;
- nested members (`x.Member is Sub`) get the same per-member discriminator;
- a downcast member read (`(x.Member as Sub).Prop`) translates to a
  subtype-projected field path;
- the closed set of participating subtypes is supplied by an opt-in registration
  (attribute and/or fluent), so discovery stays bounded.

The framing matters: **polymorphism cannot survive into a flat-field filter — it
can only be lowered to a discriminator before evaluation.** The only open question
is *where* that lowering lives. Today every consumer must do it in a private
expression preprocessor. SiftQL already owns the equivalent lowering for the root
subject; this issue asks it to own the general case so consumers stop reinventing
it.

## Motivating example

A combat mutation hook exposes two combatants, each of which may be a player or a
monster (or absent). Plugin authors want to write weapon/skill rules that branch
on the *kind* of each side:

```csharp
// "instakill a monster, but never a player"
hooks.Hook<CombatResult>()
    .Where(static ev =>
        ev.IsLongAttack
        && (ev.Attacker as Player)!.EquippedItemIds.Contains(itemId)   // attacker is a player holding the bow
        && ev.Defender is Monster)                                     // victim is a monster
    .OnHook(static ev => ev.Damage = Divine);
```

The author's mental model is a small closed hierarchy:

```csharp
public interface ICombatant { }
public sealed record Player(long CharacterId, int[] EquippedItemIds /* ... */) : ICombatant;
public sealed record Monster(int TemplateId, int Level, bool IsBoss /* ... */) : ICombatant;
// on the event:
public ICombatant? Attacker { get; init; }
public ICombatant? Defender { get; init; }
```

Both `ev.Defender is Monster` and `(ev.Attacker as Player)!.X` are valid
expression-tree nodes (they compile in a `.Where` lambda). They are simply not
translatable today, so the author either falls back to a stringly-typed
discriminator field (`ev.DefenderKind == CombatantKind.Monster`) or maintains a
private rewriter. The first leaks an implementation detail into every predicate;
the second is duplicated per consumer.

## Current behavior (verified against HEAD `83da11e`)

### `is` throws

`KernelExpressionTranslator.Translate` switches on `expression.NodeType` and has no
arm for `ExpressionType.TypeIs`, so it hits the default:

```csharp
// src/Abstractions/Translation/KernelExpressionTranslator.cs
return expression.NodeType switch
{
    ExpressionType.AndAlso => ...,
    // ... no ExpressionType.TypeIs ...
    ExpressionType.Call => TranslateMethodCall(...),
    ExpressionType.MemberAccess => TranslateBooleanField(...),
    _ => throw Unsupported(expression),                 // ← x is Monster lands here
};
```

`Unsupported` / `Explain` produce: *"Unsupported server kernel expression
'(ev.Defender Is Monster)'. Filter predicates support field comparisons (==, !=,
<, <=, >, >=), &&, ||, !, Contains, StartsWith, EndsWith, IsNullOrEmpty, In,
Exists, and Any."*

### Downcast `as` throws

`StripConvert` unwraps `Convert` / `ConvertChecked` / `TypeAs` unconditionally, but
`ValidateFieldConversion` runs first inside `TryGetFieldPath` and rejects any
cast over a parameter-referencing operand whose target type is not stripaable:

```csharp
// src/Abstractions/Translation/ExpressionTranslationHelpers.cs
private static bool CanStripFieldConversion(Type operandType, Type targetType)
{
    // allows: identity, target == object, UPcast (target.IsAssignableFrom(source)),
    //         enum→backing, exact numeric widening
    // rejects: a DOWNcast (source is the base, target the derived subtype)
}
```

So `(ev.Attacker as Player)` — `source = ICombatant`, `target = Player` — is not
strippable and throws.

### What already exists and is reusable

1. **Root-subject runtime-type discrimination.** `FilterSchemaFallbackBuilder.Build`
   seeds the schema with two virtual scalar fields:

   ```csharp
   // src/SiftQL/Schema/FilterSchemaFallbackBuilder.cs
   BuildVirtualField(subjectType, "subjectType", static t => t.FullName ?? t.Name),
   BuildVirtualField(subjectType, "subjectName", static t => t.Name),
   ```

   `BuildVirtualField` sets `bool dynamicValue = !subjectType.IsSealed;` — for a
   non-sealed (polymorphic) subject the value is resolved per instance via
   `subject.GetType()`. So `subjectName` / `subjectType` are *already* a working
   runtime discriminator for the root subject; the `is` operator is simply not
   wired to them. (`subjectType` / `subjectName` are reserved names — a real
   property of that name throws a collision today.)

2. **Operator-to-presence lowering.** `FilterNullCheck` recognizes a
   `Compare(field, Equal|NotEqual, null)` over a non-scalar (`Object` / `Array`)
   field and lowers it to `Exists` / `Not(Exists)` rather than failing the
   "not scalar" guard:

   ```csharp
   // src/SiftQL/Compiler/FilterNullCheck.cs
   public static bool IsPresenceCheck(FilterField field, FilterExpression expr) =>
       field.Kind != FilterFieldKind.Scalar &&
       expr.Operator is FilterOperator.Equal or FilterOperator.NotEqual &&
       expr.Value is { Kind: FilterValueKind.Null };
   ```

   This is the precedent for "an operator over a structurally-typed field lowers to
   a different primitive." `is` lowering is the same shape.

## Goals

1. Translate `x is Sub` / `x is not Sub` over a registered polymorphic member into
   a discriminator comparison.
2. Translate `(x.Member as Sub).Scalar` / `((Sub)x.Member).Scalar` member reads
   into a subtype-projected field path.
3. Reuse the existing `subjectType` / `subjectName` virtual-field machinery and the
   `FilterNullCheck` presence pattern rather than inventing a parallel model.
4. Keep discovery bounded and opt-in. SiftQL must not scan assemblies for every
   possible subtype; the participating subtypes are declared.
5. Keep the lowered form fully serializable as ordinary SiftQL filter data (a
   `Compare` on a scalar/virtual field, or a presence primitive) — no new wire node
   required for the common case.
6. Preserve all current behavior. Unregistered `is` / downcast-`as` keep throwing
   exactly as today.
7. Keep filter evaluation null-safe: a subtype-projected read against a subject of
   the wrong subtype does not match and does not throw (mirrors member-read
   null-propagation and `FilterNullCheck` semantics).

## Non-goals

- No pattern variables (`x is Sub s`) — C# forbids these in expression-tree lambdas
  anyway.
- No `switch` expressions / patterns, relational patterns, or exhaustiveness.
- No reflection over arbitrary loaded assemblies; only registered subtypes
  participate.
- No execution of user methods or subtype constructors during translation or
  evaluation.
- No attempt to make a base member's *base* properties subtype-specific; only the
  declared subtype's additional members are projected under the subtype path.
- No open-world polymorphism. A subtype not in the registration is treated as
  unsupported (current throwing behavior), not silently ignored.

## Proposed behavior

Three tiers, implementable independently and in order. Tiers 1–2 are small and
reuse existing machinery; Tier 3 is the larger piece.

### Tier 1 — root subject type test

`subject is Sub` (and `is not`) lowers to a comparison on the existing discriminator
virtual field:

```text
ev is Monster        →  Compare("subjectName", Equal, "Monster")
ev is not Monster     →  Not(Compare("subjectName", Equal, "Monster"))
```

This is almost free: the field already exists and is already dynamic for non-sealed
subjects. The only change is a `TypeIs` arm in the translator that emits the
compare. Use the registered discriminator id when one is configured (see
Registration), otherwise fall back to `type.Name` to match `subjectName`.

### Tier 2 — nested member type test

Give every `Object`-kind field its own discriminator virtual field, then lower a
member type test to a comparison on it:

```text
ev.Defender is Monster   →  Compare("Defender.subjectName", Equal, "Monster")
ev.Defender is not Player →  Not(Compare("Defender.subjectName", Equal, "Player"))
```

`Defender.subjectName` is produced the same way `subjectName` is for the root: a
virtual scalar field whose value is `runtimeMemberValue?.GetType().Name` (absent
when the member is null). Equivalent to `FilterNullCheck` semantics, a null member
simply does not match any concrete-subtype test.

Symmetry worth supporting: `(ev.Defender as Monster) != null` and
`(ev.Defender as Monster) == null` mean exactly `ev.Defender is Monster` /
`is not`, and should lower identically.

### Tier 3 — subtype-projected member reads

A downcast followed by a scalar/array member read projects the subtype's extra
members under a subtype-qualified path, guarded by the discriminator:

```text
(ev.Defender as Monster)!.Level          →  field "Defender.<monster>.Level"   (scalar)
((Monster)ev.Defender).IsBoss            →  field "Defender.<monster>.IsBoss"  (scalar)
(ev.Attacker as Player)!.EquippedItemIds.Contains(x)
                                         →  Contains("Attacker.<player>.EquippedItemIds", x)
```

The subtype-projected fields are ordinary `Scalar` / `Array` / `Object` fields in
the schema, discovered by running the existing `AddProperties` walk over each
registered subtype under a subtype-qualified prefix. They serialize like any other
field. Reading one against a subject of a different subtype yields "absent" (no
match), never an error — so in a filter the author can write
`(ev.Defender as Monster)!.Level > 50` and it is null-safe even though the literal
C# would `NullReferenceException` if executed against a player. (This filter-vs-
execute asymmetry is inherent to lowering `as`; it should be documented, not
hidden.)

**Path format.** A subtype-qualified separator avoids collisions when two subtypes
share a member name (e.g. both `Player` and `Monster` expose `Level` with different
meaning). The exact spelling is a design detail; the requirement is that
`Defender` as `Player` and `Defender` as `Monster` occupy distinct field
namespaces and that the segment cannot collide with a real property name (the same
guarantee `subjectType` / `subjectName` get via the reserved-name check).

### Registration

The participating subtypes must be declared so schema building stays bounded.
Mirror the attribute style already used by the query-context contracts proposal,
with a fluent fallback for types the author cannot annotate:

```csharp
[SiftPolymorphic]                                  // marks a base/interface as a filter union
public interface ICombatant { }

[SiftSubtype("player")]                            // stable discriminator id (defaults to type name)
public sealed record Player(...) : ICombatant;

[SiftSubtype("monster")]
public sealed record Monster(...) : ICombatant;
```

```csharp
// fluent alternative / for third-party types:
FilterSchema.RegisterPolymorphic<ICombatant>(builder => builder
    .Subtype<Player>("player")
    .Subtype<Monster>("monster"));
```

Registration supplies: the closed subtype set (bounds Tier-3 schema expansion), and
the stable discriminator id per subtype (decouples the wire value from the CLR type
name so a rename does not silently rebind an old serialized filter — the same
robustness goal the context-contract proposal has for method ids). Absent an
explicit id, default to `type.Name` to stay consistent with `subjectName`.

A source generator could later emit the registration from the attributes (matching
the incremental-generator approach in the context-contracts issue), but a first
version can resolve registrations at schema-build time from the attributes or the
fluent calls without a generator.

### Operator mapping summary

| Author writes (in `.Where`)                         | Lowers to                                                    | Tier |
|-----------------------------------------------------|--------------------------------------------------------------|------|
| `ev is Monster`                                     | `Compare("subjectName", Equal, "monster")`                   | 1    |
| `ev is not Monster`                                 | `Not(Compare("subjectName", Equal, "monster"))`              | 1    |
| `ev.Defender is Monster`                            | `Compare("Defender.subjectName", Equal, "monster")`          | 2    |
| `(ev.Defender as Monster) != null`                  | `Compare("Defender.subjectName", Equal, "monster")`          | 2    |
| `(ev.Defender as Monster)!.Level`                   | field `Defender.<monster>.Level` (scalar)                    | 3    |
| `(ev.Attacker as Player)!.Ids.Contains(x)`          | `Contains("Attacker.<player>.Ids", x)`                       | 3    |

## Translation details (where the code changes)

- **`KernelExpressionTranslator`** — add an `ExpressionType.TypeIs` arm to
  `Translate` that resolves the operand to a field path, the `TypeOperand` to a
  registered discriminator id, and emits the `Compare` (Tiers 1–2). Extend `Explain`
  so the unsupported message mentions type tests when a type is *not* registered.
- **`ExpressionTranslationHelpers`** — today `ValidateFieldConversion` rejects a
  downcast and `StripConvert` blindly unwraps `TypeAs`. For a `TypeAs` /
  `Convert` over a parameter member whose target is a *registered subtype*, treat
  the cast as a field-path segment (subtype projection) instead of an error
  (Tier 3). All other downcasts keep throwing.
- **`TryGetFieldPath`** — when walking the member stack, recognize a
  registered-subtype cast node and emit the subtype-qualified prefix for the
  following members.
- **`FilterSchemaFallbackBuilder`** — for a registered polymorphic member, (a) emit
  a `<member>.subjectName` / `<member>.subjectType` virtual field (Tier 2,
  reusing `BuildVirtualField`), and (b) run `AddProperties` over each registered
  subtype under the subtype-qualified prefix (Tier 3). Keep the reserved-name
  collision guard.
- **Presence reuse** — `(x as Sub) == null` / `!= null` can route through the same
  recognition as `x is not Sub` / `x is Sub`; `FilterNullCheck` already models the
  Equal/NotEqual-to-presence idea.

No new `FilterExpression` node kind is required for Tiers 1–2 (they are plain
`Compare` on a virtual scalar field). Tier 3 introduces only ordinary
scalar/array/object fields under new paths.

## Compatibility

1. Purely additive and opt-in. With no `[SiftPolymorphic]` / `RegisterPolymorphic`,
   behavior is unchanged: `is` and downcast-`as` throw exactly as today.
2. `subjectType` / `subjectName` semantics are unchanged; Tier 2 reuses the same
   builder for nested members.
3. Lowered output is ordinary serializable filter data — a `Compare` on a (virtual)
   scalar field, a presence primitive, or a normal field path. Existing serialized
   pipelines are unaffected; new ones round-trip through the existing DSL.
4. Stable discriminator ids keep an old serialized `subjectName == "monster"`
   filter valid across a CLR type rename when an explicit id is configured.

## Testing

- `is` / `is not` over the root subject → discriminator compare; matches the right
  runtime subtype, excludes others.
- `member is Sub` / `is not Sub`; null member matches no concrete-subtype test.
- `(member as Sub) == null` / `!= null` lowers identically to `is not` / `is`.
- Subtype-projected scalar read (`(x as Sub)!.Scalar > k`), array read with
  `Contains`, and a same-named member on two subtypes resolving to distinct fields.
- Subtype-projected read against the wrong subtype → no match, no throw.
- Unregistered subtype → still `Unsupported`, with an `Explain` message pointing at
  registration.
- Reserved-name collision when a subtype exposes `subjectName` / `subjectType`.
- Serialization round-trip of every lowered form through the DSL.
- Schema-build boundedness: only registered subtypes expand; depth guard
  (`depth > 3`) still applies under subtype prefixes.

## Acceptance criteria

- `x is Sub` / `x is not Sub` translate for registered unions at root and nested
  member positions.
- `(x.Member as Sub).Scalar` and `((Sub)x.Member).Scalar` translate to
  subtype-projected fields; array members compose with `Contains` / `In` / `Any`.
- Unregistered type operations keep throwing with a clear, registration-pointing
  message.
- Lowered forms reuse `subjectName` / `subjectType` and `Exists`/presence semantics
  rather than introducing a parallel discriminator concept, and serialize as
  ordinary filter data.
- Filter evaluation of a wrong-subtype projected read is null-safe.
- Subtype discovery is bounded to the declared registration; no assembly scanning.
