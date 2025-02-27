using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UpdateNotifier.Data.Models;

[Table("Games")]
public class Game : IComparable<Game>, IComparable, IEquatable<Game>
{
	[Key] public ulong GameId { get; set; }

	public             string     Title       { get; set; }
	public             DateTime   LastUpdated { get; set; }
	public             string     Url         { get; set; }
	[NotMapped] public List<User> Watchers    { get; set; } = [];

	public int CompareTo(object? obj)
	{
		if (obj is null) return 1;
		if (ReferenceEquals(this, obj)) return 0;
		return obj is Game other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(Game)}");
	}

	public int CompareTo(Game? other)
	{
		if (ReferenceEquals(this, other)) return 0;
		if (other is null) return 1;
		return LastUpdated.CompareTo(other.LastUpdated);
	}

	public bool Equals(Game? other)
	{
		if (other is null) return false;
		if (ReferenceEquals(this, other)) return true;
		return GameId == other.GameId;
	}

	public override bool Equals(object? obj)
	{
		if (obj is null) return false;
		if (ReferenceEquals(this, obj)) return true;
		if (obj.GetType() != GetType()) return false;
		return Equals((Game)obj);
	}

	public override int GetHashCode() => GameId.GetHashCode();

	public static bool operator ==(Game? left, Game? right) => Equals(left, right);

	public static bool operator !=(Game? left, Game? right) => !Equals(left, right);

	public static bool operator <(Game? left, Game? right) => Comparer<Game>.Default.Compare(left, right) < 0;

	public static bool operator >(Game? left, Game? right) => Comparer<Game>.Default.Compare(left, right) > 0;

	public static bool operator <=(Game? left, Game? right) => Comparer<Game>.Default.Compare(left, right) <= 0;

	public static bool operator >=(Game? left, Game? right) => Comparer<Game>.Default.Compare(left, right) >= 0;

	public override string ToString() => Title;
}