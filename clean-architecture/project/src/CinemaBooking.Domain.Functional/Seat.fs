module CinemaBooking.Domain.Functional.Seat

open CinemaBooking.Domain.Functional.ClientId
open CinemaBooking.Domain.Functional.SeatNumber.SeatNumber

module Seat = 
    // Само место можем представить в качестве размеченного объединения (Discriminated Union)
    type Seat =
        | FreeSeat of Number: SeatNumber
        | BookedSeat of Number: SeatNumber * ClientId: ClientId
        | BoughtSeat of Number: SeatNumber * ClientId: ClientId

    let number seat =
        match seat with
        | FreeSeat number -> number
        | BookedSeat(number, _) -> number
        | BoughtSeat(number, _) -> number

    // Функции-конструкторы
    let free (number: SeatNumber) : Seat = FreeSeat number
    let booked (number: SeatNumber, clientId: ClientId) : Seat = BookedSeat(number, clientId)
    let bought (number: SeatNumber, clientId: ClientId) : Seat = BoughtSeat(number, clientId)

    type TransitionError =
        | SeatBought of ClientId: ClientId
        | SeatBooked of ClientId: ClientId

    // Покупка места
    let buy clientId seat =
        match seat with
        | FreeSeat number -> Ok(BoughtSeat(number, clientId))
        | BookedSeat(number, bookedClientId) when (bookedClientId = clientId) -> Ok(BoughtSeat(number, clientId))
        | BookedSeat(_, bookedClientId) -> Error(TransitionError.SeatBooked bookedClientId)
        | BoughtSeat(_, boughtClientId) when (clientId = boughtClientId) -> Ok seat
        | BoughtSeat(_, boughtClientId) -> Error(TransitionError.SeatBought boughtClientId)

    // Бронирование места
    let book clientId seat : Result<Seat, TransitionError> =
        match seat with
        | FreeSeat number -> Ok(BookedSeat(number, clientId))
        | BookedSeat(_, bookedClientId) when (clientId = bookedClientId) -> Ok seat
        | BookedSeat(_, bookedClientId) -> Error(TransitionError.SeatBooked bookedClientId)
        | BoughtSeat(_, boughtClientId) -> Error(TransitionError.SeatBought boughtClientId)
