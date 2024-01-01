namespace CinemaBooking.Domain;

public abstract class Seat
{
    /// <summary>
    /// Номер места в зале
    /// </summary>
    public int Number { get; }
    protected internal Seat(int number)
    {
        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "Номер места должно быть положительным");
        }
        Number = number;
    }
    
    public abstract T Accept<T>(ISeatVisitor<T> visitor);
}