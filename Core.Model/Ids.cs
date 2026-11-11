namespace MmaManager.Core.Model;

// Strongly-typed ids. Wrapping each id in its own value type makes it a
// compile error to pass a FighterId where a PromotionId is expected, keeps
// them cheap (stack-allocated), and gives correct equality/hashing for use
// as dictionary keys for free (record struct).
//
// Convention: Value 0 is "unset" — default(FighterId) means "no fighter".
// The World id generators start handing out ids at 1.

public readonly record struct FighterId(int Value) : IEntityId;
public readonly record struct PromotionId(int Value) : IEntityId;
public readonly record struct EventId(int Value) : IEntityId;
public readonly record struct FightId(int Value) : IEntityId;
public readonly record struct TitleId(int Value) : IEntityId;
public readonly record struct CountryId(int Value) : IEntityId;
public readonly record struct CityId(int Value) : IEntityId;
public readonly record struct VenueId(int Value) : IEntityId;
public readonly record struct CampId(int Value) : IEntityId;
