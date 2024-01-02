namespace CinemaBooking.Domain.Functional

open Microsoft.FSharp.Core


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

type ClientId = int32

module Seat =
    open SeatNumber
    
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


module Session =
    open Seat
    
    type SessionId = int32

    type Session =
            { Id: SessionId
              Seats: Seat.Seat list }

    // Создать новый сеанс
    let create id seats : Session = { Id = id; Seats = seats }
    
    // Найти указанное место на сеансе
    let tryFindSeat seatNumber session =
        List.tryFind (fun seat -> number seat = seatNumber) session.Seats
    
    // Обновить место на сеансе
    let setSeat session seat =
        {session with Seats = session.Seats
                                     |> List.map (fun s -> if number s = number seat then seat else s)}
    
    type OperationError =
        | SeatNotFound
        | SeatBooked of ClientId: ClientId
        | SeatBought of ClientId: ClientId

    let makeSeatTransition (operation: Seat -> Result<Seat, TransitionError>) seat =
        match operation seat with
        | Ok newSeat -> Ok newSeat
        | Error e -> match e with
                     | TransitionError.SeatBought clientId -> Error (OperationError.SeatBought clientId)
                     | TransitionError.SeatBooked clientId -> Error (OperationError.SeatBooked clientId)
    
    // Покупка места на сеансе
    let buy session seatNumber clientId =
        match session |> tryFindSeat seatNumber with
        | None -> Error SeatNotFound
        | Some seat -> seat
                        |> makeSeatTransition (buy clientId)
                        |> Result.map (setSeat session)
    
    // Бронирование места на сеансе
    let book session seatNumber clientId =
        match session |> tryFindSeat seatNumber with
        | None -> Error SeatNotFound
        | Some seat -> seat
                        |> makeSeatTransition (book clientId)
                        |> Result.map (setSeat session)



module SeatService =    
    open Session
    
    // Вместо интерфейсов - отдельные функции
    type SessionFinder = SessionId -> Session option
    type SessionUpdater = Session -> Unit
    
    type ServiceError =
        | SessionNotFound
        | SeatNotFound
        | SeatBooked of ClientId: ClientId
        | SeatBought of ClientId: ClientId
        
    let toOperationError e =
        match e with
        | OperationError.SeatNotFound -> ServiceError.SeatNotFound
        | OperationError.SeatBooked clientId -> ServiceError.SeatBooked clientId
        | OperationError.SeatBought clientId -> ServiceError.SeatBought clientId
    
    // Передаем функции-интерфейсы в качестве непосредственных аргументов для функции
    let book sessionId number clientId sessionFinder sessionUpdater =
        let session = sessionFinder sessionId
        match session with
        | None -> Error SessionNotFound
        | Some session -> book session number clientId
                          |> Result.mapError toOperationError
                          |> Result.map (fun s -> sessionUpdater s; s) 
                          
    let buy sessionId number clientId sessionFinder sessionUpdater =
        let session = sessionFinder sessionId
        match session with
        | None -> Error SessionNotFound
        | Some session -> buy session number clientId
                          |> Result.mapError toOperationError
                          |> Result.map (fun s -> sessionUpdater s; s)                        