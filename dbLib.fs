namespace VarietyTranslator

open Microsoft.Data.Sqlite
open System

// Чистая структура данных для передачи в UI слой
type Variety = {
    Id: int
    SciName: string
    Variety: string
    Color: string
}

type VarietyFull = {
    Id: int
    SciName: string
    Variety: string
    Color: string
    SciNameRu: string
    VarietyRu: string
    ColorRu: string
}

module dbLib =
    let loadVarieties (connectionString: string) (companyFilter: string) (searchTerm: string) : Variety list =
        // Соединение закроется автоматически при выходе из функции
        use connection = new SqliteConnection(connectionString)
        connection.Open()
        
        let companyClause = 
            if companyFilter = "Все" then "" 
            else "company = @company AND "
        
        use command = connection.CreateCommand()
        command.CommandText <- 
            "SELECT id, sci_name, variety, color FROM varieties
             WHERE " + companyClause + "
             (sci_name LIKE '%' || @search || '%'
                    OR variety LIKE '%' || @search || '%'
                    OR color LIKE '%' || @search || '%')
             ORDER BY \"company\", \"sci_name\", \"variety\", \"color\";"
            
        if companyFilter <> "Все" then
            command.Parameters.AddWithValue("@company", companyFilter) |> ignore    
        
        command.Parameters.AddWithValue("@search", searchTerm) |> ignore
        
        // Ридер также безопасно закроется внутри функции
        use reader = command.ExecuteReader()
        
        // Собираем данные в промежуточный список
        let list = System.Collections.Generic.List<Variety>()
        
        while reader.Read() do
            let id = reader.GetInt32(0)
            let sciName = if reader.IsDBNull(1) then "" else reader.GetString(1)
            let variety = if reader.IsDBNull(2) then "" else reader.GetString(2)
            let color = if reader.IsDBNull(3) then "" else reader.GetString(3)
            
            list.Add({ Id = id; SciName = sciName; Variety = variety; Color = color })
        
        // Возвращаем чистый F# список, который можно безопасно использовать в UI
        List.ofSeq list
        
    let loadByIds (connectionString: string) (ids: int list) : VarietyFull list =
        let idsStr = 
            ids
            |> List.indexed
            |> List.map (fun (i, x) -> x, i+1)
            |> List.map string
            |> String.concat ","

        use connection = new SqliteConnection(connectionString)
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <-
            $"WITH list(id, ord) AS (VALUES {idsStr} )
            SELECT 
                v.id, 
                v.sci_name, 
                v.variety, 
                v.color, 
                v.sci_name_ru, 
                v.variety_ru, 
                v.color_ru 
            FROM list l
            JOIN varieties v ON v.id = l.id
            ORDER BY l.ord;"

        use reader = command.ExecuteReader()
        let list = System.Collections.Generic.List<VarietyFull>()

        while reader.Read() do
            let id = reader.GetInt32(0)
            let sciName = if reader.IsDBNull(1) then "" else reader.GetString(1)
            let variety = if reader.IsDBNull(2) then "" else reader.GetString(2)
            let color = if reader.IsDBNull(3) then "" else reader.GetString(3)
            let sciNameRu = if reader.IsDBNull(4) then "" else reader.GetString(4)
            let varietyRu = if reader.IsDBNull(5) then "" else reader.GetString(5)
            let colorRu = if reader.IsDBNull(6) then "" else reader.GetString(6)
            list.Add({ Id = id; SciName = sciName; Variety = variety; Color = color; SciNameRu = sciNameRu; VarietyRu = varietyRu; ColorRu = colorRu })
        
        List.ofSeq list