namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// Full names of real, notable people (fighters, athletes, politicians, celebrities, historical figures)
/// that a name pool, with or without the reserve, can spell out. Everyone in this world is fictional, so
/// <see cref="NameRegistry"/> treats every one of these as already taken in both modes: a generated person
/// who rolls one counts as a collision and is resolved like any other. Found by going through every
/// culture's base x base, base x reserve, reserve x base and reserve x reserve pairs for names that read as
/// a real person; best effort, not exhaustive, so a new pool or reserve entry should be checked against the
/// same eye. Append-only. Not for display.
/// </summary>
internal static class RealNames
{
    public static IReadOnlyCollection<string> All => Names;

    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        // Latin America
        "Pablo Escobar", "Humberto Soto", "Fernando Vargas", "Adrian Yanez", "Gabriel Medina", "Oscar Valdez",
        "Luis Arce", "Pedro Castillo", "Alvaro Uribe", "Camilo Villegas", "Ricardo Lara", "Manuel Contreras",
        "Zurab Tsereteli",
        // Turkic
        "Alp Arslan", "Furkan Korkmaz", "Cem Yilmaz", "Burak Yilmaz",
        // Russian and Caucasus
        "Ivan Pavlov", "Denis Lebedev", "Oleg Popov", "Aleksander Popov", "Artem Lebedev", "Sergei Lebedev", "Yuri Orlov",
        "Tigran Petrosyan",
        // India
        "Deepak Chopra", "Yash Chopra", "Dev Patel", "Sanjay Gupta", "Manish Malhotra", "Rohan Joshi", "Karan Singh",
        "Vijay Singh", "Arjun Singh", "Ravi Patel", "Gaurav Gupta",
        // Southern Africa
        "Pieter Botha", "Johan Botha", "Siyabonga Cele",
        // German
        "Konrad Lorenz", "Sebastian Koch", "Stefan Schwarz", "Moritz Bauer",
        // West Africa
        "Moussa Traore", "Babatunde Fashola", "Kwesi Appiah", "Kwame Appiah", "Ousmane Sow", "Moussa Sow", "Sekou Toure",
        "Emeka Okafor",
        // Japan
        "Satoshi Ishii", "Kosei Inoue", "Kenta Kobayashi", "Ryo Ishikawa", "Jun Maeda", "Takumi Saito", "Koji Kondo",
        // Brazil
        "Davi Ramos", "Leonardo Santos", "Ricardo Ramos", "Vitor Ribeiro", "Ricardo Almeida", "Rafael Silva", "Thiago Silva",
        "Thiago Santos", "Thiago Pereira", "Bruno Silva", "Andre Silva", "Felipe Melo", "Everton Ribeiro", "Fernando Santos",
        "Alex Pereira", "Claudio Silva", "Vitor Pereira", "Ricardo Pereira", "Lucas Moura", "Marcio Santos", "Rafael Cardoso",
        // Anglo
        "Lance Briggs", "Brock Holt", "Grant Holt", "Miles Sanders", "Shane Dawson", "Trent Dalton",
        // Ireland, Netherlands, France, Poland, Nordic, Italy, Iberia, Balkans, Arab world, Pacific, Korea, China, Czechia, Hungary
        "Liam Gallagher", "Niall Quinn", "Cillian Murphy", "Kieran Tierney", "Sven Kramer", "Jeroen Smit", "Remy Girard",
        "Michal Kaminski", "Piotr Nowak", "Marek Kaminski", "Henrik Larsen", "Lars Larsen", "Anders Holm", "Erik Karlsson",
        "Alessandro Greco", "Matteo Ricci", "Raul Reyes", "Pablo Iglesias", "Nemanja Vidic", "Nemanja Matic", "Darko Kovacevic",
        "Tariq Aziz", "Ibrahim Maalouf", "Hone Heke", "Wiremu Tamihana", "Minho Choi", "Minho Lee", "Bo Yang",
        "Karel Svoboda", "Adam Nagy",
    };
}
