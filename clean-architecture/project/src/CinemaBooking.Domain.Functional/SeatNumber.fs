module CinemaBooking.Domain.Functional.SeatNumber

// Для корректного представления номера места создадим отдельный тип данных
module SeatNumber =
    type SeatNumber = private SeatNumber of int

    let number (seat: SeatNumber) : int =
        match seat with
        | SeatNumber(n) -> n

    let create number =
        if number < 1 then
            Option.None
        else
            Option.Some(SeatNumber number)