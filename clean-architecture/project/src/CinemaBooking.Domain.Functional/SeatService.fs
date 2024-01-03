module CinemaBooking.Domain.Functional.SeatService

open CinemaBooking.Domain.Functional.ClientId

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
        | Some session ->
            book session number clientId
            |> Result.mapError toOperationError
            |> Result.map (fun s ->
                sessionUpdater s
                s)

    let buy sessionId number clientId sessionFinder sessionUpdater =
        let session = sessionFinder sessionId

        match session with
        | None -> Error SessionNotFound
        | Some session ->
            buy session number clientId
            |> Result.mapError toOperationError
            |> Result.map (fun s ->
                sessionUpdater s
                s)


