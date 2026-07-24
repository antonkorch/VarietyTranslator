namespace VarietyTranslator

open System.Security.Cryptography
open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Platform.Storage
open System
open System.IO


module private Helpers =
    // ЭКСПОРТ ДАННЫХ В EXCEL С ПОМОЩЬЮ CLOSEDXML
    let exportToExcel (filepath: string) (data: VarietyFull list) =
        use workbook = new ClosedXML.Excel.XLWorkbook()
        let worksheet = workbook.Worksheets.Add("Выборка")
        
        // Заголовки столбцов Excel
        worksheet.Cell(1, 2).Value <- "№"
        worksheet.Cell(1, 3).Value <- "Scientific name"
        worksheet.Cell(1, 4).Value <- "Variety"
        worksheet.Cell(1, 5).Value <- "Color"
        worksheet.Cell(1, 6).Value <- "Ботаническое название, русское"
        worksheet.Cell(1, 7).Value <- "Сорт"
        worksheet.Cell(1, 8).Value <- "Цвет (фонетическая транслитерация)"
        worksheet.Cell(1, 9).Value <- "Кол-во          (по заказу)"
        
        // Заполнение ячеек данными
        data |> List.iteri (fun i item ->
            let row = i + 2
            worksheet.Cell(row, 2).Value <- item.Id
            worksheet.Cell(row, 3).Value <- item.SciName
            worksheet.Cell(row, 4).Value <- item.Variety
            worksheet.Cell(row, 5).Value <- item.Color
            worksheet.Cell(row, 6).Value <- item.SciNameRu
            worksheet.Cell(row, 7).Value <- item.VarietyRu
            worksheet.Cell(row, 8).Value <- item.ColorRu
            worksheet.Cell(row, 9).Value <- 0
        )
        
        // Автоматический подбор ширины колонок по контенту
        worksheet.Columns().AdjustToContents() |> ignore
        
        // Сохраняем файл на диск
        workbook.SaveAs(filepath)
        
        
    let getSelectedCompany (comboBox: ComboBox) =
        match comboBox.SelectedItem with
        | null -> "Все"
        | :? ComboBoxItem as item -> 
            match item.Content with
            | :? string as s -> s
            | obj -> string obj
        | obj -> string obj
        
    let tryGetCheckBox (grid: Grid) =
        grid.Children 
        |> Seq.tryFind (fun c -> Grid.GetColumn(c) = 0 && (match c with :? CheckBox -> true | _ -> false))
        |> Option.map (fun c -> c :?> CheckBox)

type MainWindow() as this =
    inherit Window ()
    do this.InitializeComponent()
    
    // Метод перемещения выделенных элементов на правой панели клавиатурой через массив в памяти
    member private this.MoveSelectedItems(direction: int) =
        let selectedItemsContainer = this.FindControl<StackPanel>("SelectedItemsContainer")
        if not (isNull selectedItemsContainer) then
            // Превращаем дочерние элементы StackPanel в список F#
            let childrenList = 
                selectedItemsContainer.Children
                |> Seq.choose (fun child -> match child with :? Border as b -> Some b | _ -> None)
                |> Seq.toList
                
            let count = childrenList.Length
            if count > 1 then
                // Вспомогательная функция проверки: выделена ли строка галочкой
                let isChecked (border: Border) =
                    let grid = border.Child :?> Grid
                    match Helpers.tryGetCheckBox grid with
                    | Some cb -> cb.IsChecked.HasValue && cb.IsChecked.Value
                    | None -> false

                // Создаем изменяемый массив для безопасной перестановки элементов в памяти
                let arr = Array.ofList childrenList
                
                if direction = -1 then
                    // ДВИЖЕНИЕ ВВЕРХ: обходим элементы от индекса 1 до конца
                    for i in 1 .. count - 1 do
                        if isChecked arr.[i] then
                            // Меняем местами текущий элемент с предыдущим
                            let temp = arr.[i]
                            arr.[i] <- arr.[i - 1]
                            arr.[i - 1] <- temp
                elif direction = 1 then
                    // ДВИЖЕНИЕ ВНИЗ: обходим элементы снизу вверх (от count-2 до 0)
                    for i in (count - 2) .. -1 .. 0 do
                        if isChecked arr.[i] then
                            // Меняем местами текущий элемент со следующим
                            let temp = arr.[i]
                            arr.[i] <- arr.[i + 1]
                            arr.[i + 1] <- temp

                // Пакетно обновляем интерфейс: очищаем и добавляем в новом порядке
                selectedItemsContainer.Children.Clear()
                for border in arr do
                    selectedItemsContainer.Children.Add(border)
    
    member private this.InitializeComponent() =
        AvaloniaXamlLoader.Load(this)
        
        // Находим элементы управления по их x:Name из разметки
        let searchTextBox = this.FindControl<TextBox>("SearchTextBox")
        let availableItemsContainer = this.FindControl<StackPanel>("AvailableItemsContainer")
        let selectedItemsContainer = this.FindControl<StackPanel>("SelectedItemsContainer")
        let companyComboBox = this.FindControl<ComboBox>("CompanyComboBox")
        let searchButton = this.FindControl<Button>("SearchButton")
        let exportExcelButton = this.FindControl<Button>("ExportExcelButton")
        
        // Общая функция для запуска поиска с актуальными значениями
        let triggerSearch () =
            let textToSearch = if isNull searchTextBox.Text then "" else searchTextBox.Text
            let selectedCompany = Helpers.getSelectedCompany companyComboBox
            this.LoadItemsFromDatabase(textToSearch, selectedCompany, availableItemsContainer)
        
        // 1. Поиск по нажатию Enter в текстовом поле
        searchTextBox.KeyDown.Add(fun e ->
            if e.Key = Key.Enter then triggerSearch()
        )
        // 3. и для клика по поисковой кнопке
        searchButton.Click.Add(fun _ -> triggerSearch())
        
        // 6. УПРАВЛЕНИЕ С КЛАВИАТУРЫ ЧЕРЕЗ СТАНДАРТНЫЙ KEYDOWN.ADD С ДИАГНОСТИКОЙ
        this.KeyDown.Add(fun e ->
            if e.Key = Key.Up then
                this.MoveSelectedItems(-1) // Переместить выделенные строки вверх
                e.Handled <- true
            elif e.Key = Key.Down then
                this.MoveSelectedItems(1) // Переместить выделенные строки вниз
                e.Handled <- true
        )
        // 7. ЭКСПОРТ В EXCEL ПО КЛИКУ НА КНОПКУ "СОЗДАТЬ EXCEL"
        exportExcelButton.Click.Add(fun _ ->
            if not (isNull selectedItemsContainer) then
                // 1. Получаем список оригинальных ID из правой панели (из свойства .Tag)
                let ids = 
                    selectedItemsContainer.Children
                    |> Seq.choose (fun child -> match child with :? Border as b -> Some b | _ -> None)
                    |> Seq.choose (fun b -> match b.Tag with :? int as id -> Some id | _ -> None)
                    |> Seq.toList
                
                if List.isEmpty ids then
                    // Если правый список пуст, ничего не экспортируем
                    ()
                else
                    // Запуск асинхронной операции для выбора места сохранения
                    async {
                        // Конфигурируем настройки диалога сохранения
                        let options = FilePickerSaveOptions()
                        options.Title <- "Сохранить отчет Excel"
                        options.SuggestedFileName <- "Черновик для обращения.xlsx"
                        
                        let excelType = FilePickerFileType("Книга Excel (*.xlsx)")
                        excelType.Patterns <- [| "*.xlsx" |]
                        options.FileTypeChoices <- [| excelType |]
                        // Показываем стандартный диалог сохранения файлов в Avalonia [2.2.4]
                        let! fileRef = this.StorageProvider.SaveFilePickerAsync(options) |> Async.AwaitTask
                        // Если пользователь выбрал файл и нажал "Сохранить"
                        if not (isNull fileRef) then
                            let filepath = fileRef.Path.LocalPath
                        // 2. Получаем полный путь к БД и делаем запрос через loadByIds
                            let baseDir = AppDomain.CurrentDomain.BaseDirectory
                            let dbPath = Path.Combine(baseDir, "resources", "varieties.db")
                            let connectionString = $"Data Source={dbPath}"
                            
                            let fullVarieties = dbLib.loadByIds connectionString ids
                        // 3. Запускаем экспорт данных в Excel
                            Helpers.exportToExcel filepath fullVarieties
                    } |> Async.StartImmediate
            )
        
    member private this.LoadItemsFromDatabase(searchTerm: string, companyFilter: string, container: StackPanel) =
        container.Children.Clear()
        
        let baseDir = AppDomain.CurrentDomain.BaseDirectory
        let dbPath = Path.Combine(baseDir, "resources", "varieties.db")
        let connectionString = $"Data Source={dbPath}"
        
        let varieties = dbLib.loadVarieties connectionString companyFilter searchTerm
        // Отрисовываем элементы на основе F# списка
        for v in varieties do
            let displayName = $"{v.SciName} {v.Variety}   "
            let rowBorder = this.CreateItemRow(v.Id, displayName, v.Color, true)
            container.Children.Add(rowBorder)
            
    member private this.CreateItemRow(dbId: int, name: string, color: string, isLeftList: bool) =
        let grid = Grid()
        if isLeftList then
            grid.ColumnDefinitions <- ColumnDefinitions("40, Auto, Auto")
        else
            grid.ColumnDefinitions <- ColumnDefinitions("40, Auto, Auto, 40")
            
        let checkBox = CheckBox(
            IsChecked = Nullable(false), 
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        )
        Grid.SetColumn(checkBox, 0)
        
        let nameText = TextBlock(
            Text = name, 
            VerticalAlignment = VerticalAlignment.Center, 
            FontSize = 14.0, 
            FontWeight = Media.FontWeight.Medium
        )
        Grid.SetColumn(nameText, 1)
        
        let colorText = TextBlock(
            Text = color, 
            VerticalAlignment = VerticalAlignment.Center, 
            FontSize = 14.0, 
            Foreground = Media.Brushes.Gray
        )
        Grid.SetColumn(colorText, 2)
        
        grid.Children.Add(checkBox)
        grid.Children.Add(nameText)
        grid.Children.Add(colorText)
        
        let border = Border(Child = grid)
        border.Classes.Add("item-row-border")
        
        border.Background <- Media.Brushes.Transparent 
        border.Cursor <- Input.Cursor.Parse("Hand") 
        border.Tag <- dbId
        
        checkBox.IsCheckedChanged.Add(fun _ ->
            let isCurrentlyChecked =
                if checkBox.IsChecked.HasValue then checkBox.IsChecked.Value
                else false
            if isCurrentlyChecked then
                border.Background <- Media.Brush.Parse("#E8F5E9")
            else
                border.Background <- Media.Brushes.Transparent 
            )
        
        // Создаем кнопку-мусорку
        if not isLeftList then
            let deleteBtn = Button(
                Background = Media.Brushes.Transparent,
                BorderThickness = Thickness(0.0),
                Padding = Thickness(4.0),
                Cursor = Input.Cursor.Parse("Hand"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            )
        // Иконка ведра 
            let trashIcon = PathIcon(
                Data = Media.Geometry.Parse("M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"),
                Width = 14.0,
                Height = 14.0,
                Foreground = Media.Brush.Parse("#bf9f9d")
            )
            deleteBtn.Content <- trashIcon
            Grid.SetColumn(deleteBtn, 4)
            grid.Children.Add(deleteBtn)
            
            deleteBtn.Click.Add(fun _ ->
                let selectedItemsContainer = this.FindControl<StackPanel>("SelectedItemsContainer")
                selectedItemsContainer.Children.Remove(border) |> ignore
                )
        
        // Интерактивная обработка нажатия на строку
        border.PointerPressed.Add(fun _ ->
            if isLeftList then
                let selectedItemsContainer = this.FindControl<StackPanel>("SelectedItemsContainer")
                let newRow = this.CreateItemRow(dbId, name, color, false)
                selectedItemsContainer.Children.Add(newRow)
                checkBox.IsChecked <- Nullable(false)
                
                let scrollViewer = this.FindControl<ScrollViewer>("SelectedItemsScrollViewer")
                Threading.Dispatcher.UIThread.Post(fun () -> scrollViewer.ScrollToEnd())
            else
                let current = 
                    if checkBox.IsChecked.HasValue then checkBox.IsChecked.Value
                    else false
                checkBox.IsChecked <- Nullable(not current)
        )
        
        border