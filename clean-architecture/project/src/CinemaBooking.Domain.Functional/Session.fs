namespace CinemaBooking.Domain.Functional

open CinemaBooking.Domain.Functional.ClientId
open CinemaBooking.Domain.Functional.Seat.Seat

open Microsoft.FSharp.Core


module Session =
    type SessionId = int32

    type Session =
        { Id: SessionId
          Seats: Seat list }

    // Создать новый сеанс
    let create id seats : Session = { Id = id; Seats = seats }
    
    // Найти указанное место на сеансе
    let tryFindSeat seatNumber session =
        List.tryFind (fun seat -> number seat = seatNumber) session.Seats

    // Обновить место на сеансе
    let setSeat session seat =
        { session with
            Seats = session.Seats |> List.map (fun s -> if number s = number seat then seat else s) }

    type OperationError =
        | SeatNotFound
        | SeatBooked of ClientId: ClientId
        | SeatBought of ClientId: ClientId

    let makeSeatTransition (operation: Seat -> Result<Seat, TransitionError>) seat =
        match operation seat with
        | Ok newSeat -> Ok newSeat
        | Error e ->
            match e with
            | TransitionError.SeatBought clientId -> Error(OperationError.SeatBought clientId)
            | TransitionError.SeatBooked clientId -> Error(OperationError.SeatBooked clientId)

    // Покупка места на сеансе
    let buy session seatNumber clientId =
        match session |> tryFindSeat seatNumber with
        | None -> Error SeatNotFound
        | Some seat -> seat |> makeSeatTransition (buy clientId) |> Result.map (setSeat session)

    // Бронирование места на сеансе
    let book session seatNumber clientId =
        match session |> tryFindSeat seatNumber with
        | None -> Error SeatNotFound
        | Some seat -> seat |> makeSeatTransition (book clientId) |> Result.map (setSeat session)