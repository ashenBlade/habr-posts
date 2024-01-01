namespace CinemaBooking.Domain;

public abstract class Seat
{
    protected Seat(int number)
    {
        Number = number;
    }

    /// <summary>
    /// Номер места в зале
    /// </summary>
    public int Number { get; }

    public abstract T Accept<T>(ISeatVisitor<T> visitor);
}