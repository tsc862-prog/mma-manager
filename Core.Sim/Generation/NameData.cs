using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// Fictional name pools, keyed by region so a fighter's name reads like it
/// belongs to their nationality. Entirely invented flavour data — no real
/// people. Nicknames are region-neutral fight-game colour.
/// </summary>
internal static class NameData
{
    public static (IReadOnlyList<string> First, IReadOnlyList<string> Last) Pool(Region region) =>
        Pools.TryGetValue(region, out var p) ? p : Pools[Region.NorthAmerica];

    /// <summary>Name pool for a country's name culture (see WorldAtlas); falls back to the continental pool.</summary>
    public static (IReadOnlyList<string> First, IReadOnlyList<string> Last) Pool(string culture, Region fallback) =>
        Cultures.TryGetValue(culture, out var p) ? p : Pool(fallback);

    /// <summary>
    /// Sex-aware culture pool: women get culture-appropriate first names over the
    /// shared (culture) surnames; men use the base culture pool. Cultures without
    /// an authored female list fall back to an international women's pool.
    /// </summary>
    public static (IReadOnlyList<string> First, IReadOnlyList<string> Last) Pool(string culture, Region fallback, Sex sex)
    {
        if (sex != Sex.Female) return Pool(culture, fallback);
        var last = (Cultures.TryGetValue(culture, out var c) ? c : Pool(fallback)).Last;
        var first = FemaleFirst.TryGetValue(culture, out var ff) ? ff : InternationalFemale;
        return (first, last);
    }

    // Female first names for the higher-frequency cultures; the rest borrow the
    // international pool (still paired with their own culture's surnames).
    private static readonly IReadOnlyList<string> InternationalFemale = new[]
    {
        "Mia", "Nina", "Lena", "Sara", "Ava", "Elena", "Maya", "Ana", "Lea", "Emma",
        "Zoe", "Nadia", "Iris", "Vera", "Sofia", "Alina", "Kira", "Noa", "Talia", "Ruby",
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> FemaleFirst =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["anglo"] = new[] { "Ashley", "Brooke", "Sydney", "Paige", "Jade", "Raven", "Harlow", "Sloane", "Reese", "Quinn", "Devyn", "Kayla", "Bristol", "Marlowe" },
            ["celtic"] = new[] { "Saoirse", "Niamh", "Aoife", "Roisin", "Ciara", "Sinead", "Orla", "Maeve", "Fiadh", "Bronagh" },
            ["latino"] = new[] { "Valentina", "Camila", "Lucia", "Daniela", "Gabriela", "Paola", "Ximena", "Renata", "Antonella", "Julieta" },
            ["brazilian"] = new[] { "Amanda", "Beatriz", "Larissa", "Camila", "Juliana", "Bruna", "Leticia", "Rafaela", "Gabriela", "Vitoria" },
            ["french"] = new[] { "Camille", "Chloe", "Manon", "Ines", "Juliette", "Amelie", "Océane", "Lea", "Margaux", "Sabine" },
            ["german"] = new[] { "Lena", "Hanna", "Greta", "Lea", "Franka", "Annika", "Mila", "Svenja", "Katrin", "Ronja" },
            ["dutch"] = new[] { "Sanne", "Fleur", "Anouk", "Femke", "Lotte", "Isa", "Roos", "Nienke", "Maud", "Bente" },
            ["iberian"] = new[] { "Lucia", "Marta", "Carmen", "Ainhoa", "Paula", "Sara", "Nerea", "Alba", "Irene", "Rocio" },
            ["italian"] = new[] { "Giulia", "Chiara", "Francesca", "Alessia", "Valentina", "Martina", "Serena", "Bianca", "Noemi", "Sofia" },
            ["scandinavian"] = new[] { "Freya", "Ingrid", "Astrid", "Sigrid", "Maja", "Elin", "Saga", "Nora", "Thea", "Liv" },
            ["polish"] = new[] { "Magda", "Kasia", "Ania", "Ola", "Zofia", "Weronika", "Ewa", "Marta", "Natalia", "Julia" },
            ["czech"] = new[] { "Tereza", "Katerina", "Barbora", "Lucie", "Petra", "Veronika", "Eliska", "Karolina", "Marketa", "Nikola" },
            ["hungarian"] = new[] { "Reka", "Zsofia", "Anna", "Petra", "Dora", "Fanni", "Eszter", "Boglarka", "Kata", "Nora" },
            ["romanian"] = new[] { "Ioana", "Andreea", "Elena", "Bianca", "Gabriela", "Alina", "Daria", "Roxana", "Larisa", "Cristina" },
            ["balkan"] = new[] { "Ana", "Milica", "Jelena", "Ivana", "Marija", "Sara", "Katarina", "Tijana", "Nina", "Dragana" },
            ["greek"] = new[] { "Eleni", "Maria", "Katerina", "Sofia", "Ioanna", "Despina", "Vasiliki", "Georgia", "Anna", "Dimitra" },
            ["russian"] = new[] { "Anastasia", "Ekaterina", "Yulia", "Natalia", "Irina", "Svetlana", "Olga", "Daria", "Marina", "Valeria" },
            ["georgian"] = new[] { "Nino", "Tamar", "Salome", "Ana", "Mariam", "Ketevan", "Tinatin", "Lika", "Nana", "Eka" },
            ["armenian"] = new[] { "Ani", "Lusine", "Mariam", "Nare", "Gohar", "Sona", "Anahit", "Lilit", "Syuzanna", "Meline" },
            ["turkic"] = new[] { "Elif", "Zeynep", "Ayse", "Merve", "Dilara", "Ece", "Sevil", "Leyla", "Nur", "Aylin" },
            ["arabic"] = new[] { "Layla", "Amira", "Nour", "Yasmin", "Salma", "Rana", "Dana", "Hana", "Lina", "Maya" },
            ["persian"] = new[] { "Setareh", "Yasaman", "Niloufar", "Parisa", "Shirin", "Roya", "Nazanin", "Mahsa", "Sahar", "Leila" },
            ["israeli"] = new[] { "Noa", "Tamar", "Maya", "Shira", "Yael", "Adi", "Hila", "Roni", "Michal", "Talia" },
            ["japanese"] = new[] { "Yui", "Aoi", "Rin", "Hana", "Mei", "Saki", "Nao", "Emi", "Kana", "Riko" },
            ["korean"] = new[] { "Jiwoo", "Seoyeon", "Minji", "Hana", "Yuna", "Soyeon", "Eunji", "Haeun", "Jimin", "Sora" },
            ["chinese"] = new[] { "Mei", "Ling", "Xiao", "Fang", "Yan", "Jing", "Hui", "Na", "Ying", "Lan" },
            ["mongolian"] = new[] { "Saran", "Oyun", "Tuya", "Nomin", "Enkhjin", "Bolor", "Sarnai", "Anu", "Gerel", "Khulan" },
            ["thai"] = new[] { "Ploy", "Nun", "Fah", "Mook", "Bua", "Praew", "Som", "Namfon", "Ying", "Kwan" },
            ["vietnamese"] = new[] { "Linh", "Mai", "Huong", "Trang", "Thao", "Ngoc", "Anh", "Ha", "Yen", "Nhung" },
            ["filipino"] = new[] { "Maria", "Angel", "Kristine", "Jasmine", "Camille", "Bea", "Andrea", "Nicole", "Joy", "Divine" },
            ["indonesian"] = new[] { "Putri", "Sari", "Ayu", "Dewi", "Indah", "Rina", "Wulan", "Fitri", "Ratna", "Siti" },
            ["indian"] = new[] { "Priya", "Ananya", "Divya", "Kavya", "Neha", "Riya", "Meera", "Pooja", "Sneha", "Aditi" },
            ["centralasian"] = new[] { "Aigerim", "Dana", "Aizhan", "Madina", "Alina", "Zarina", "Gulnara", "Aida", "Kamila", "Nazerke" },
            ["westafrican"] = new[] { "Amara", "Chioma", "Ada", "Zainab", "Fatima", "Ngozi", "Aisha", "Efua", "Yaa", "Folake" },
            ["eastafrican"] = new[] { "Amani", "Zawadi", "Halima", "Neema", "Sania", "Wanjiru", "Aster", "Nyah", "Imani", "Zuri" },
            ["centralafrican"] = new[] { "Grace", "Esperance", "Divine", "Nadege", "Clarisse", "Bijou", "Sarah", "Naomi", "Charlene", "Yvette" },
            ["southernafrican"] = new[] { "Thandiwe", "Nomvula", "Zanele", "Lerato", "Naledi", "Ayanda", "Buhle", "Palesa", "Refilwe", "Zola" },
            ["polynesian"] = new[] { "Leilani", "Moana", "Hina", "Mareva", "Ana", "Tiare", "Vaimiti", "Sina", "Mele", "Ariana" },
        };

    // Per-culture pools so a Georgian reads Georgian and a Thai reads Thai.
    // Same rule as above: invented combinations only, no real people.
    private static readonly IReadOnlyDictionary<string, (IReadOnlyList<string> First, IReadOnlyList<string> Last)> Cultures =
        new Dictionary<string, (IReadOnlyList<string>, IReadOnlyList<string>)>(StringComparer.Ordinal)
        {
            ["anglo"] = (
                new[] { "Cody", "Tyrell", "Marcus", "Jalen", "Brody", "Devin", "Trent", "Shane", "Dominic", "Kade", "Rashad", "Colton", "Miles", "Darnell", "Wyatt", "Chance", "Isaiah", "Grant" },
                new[] { "Harker", "Boone", "Whitlock", "Sanders", "Cross", "Maddox", "Vance", "Holt", "Briggs", "Nash", "Dunn", "Ferris", "Mercer", "Colby", "Radley", "Stanton" }),
            ["celtic"] = (
                new[] { "Cillian", "Niall", "Sean", "Declan", "Cormac", "Ronan", "Eoin", "Padraig", "Fergal", "Darragh", "Cathal", "Liam" },
                new[] { "OShea", "Halloran", "Gallagher", "Brennan", "McKenna", "Doyle", "ORourke", "Keane", "Flanagan", "Duffy", "Nolan", "Quinn" }),
            ["latino"] = (
                new[] { "Diego", "Joaquin", "Emiliano", "Rodrigo", "Nahuel", "Facundo", "Santiago", "Mateo", "Cristobal", "Alvaro", "Hernan", "Ignacio" },
                new[] { "Moreno", "Vargas", "Quintero", "Sosa", "Ibarra", "Salazar", "Paredes", "Fuentes", "Delgado", "Rojas", "Carrasco", "Medina" }),
            ["brazilian"] = (
                new[] { "Rafael", "Thiago", "Vinicius", "Mateus", "Caio", "Bruno", "Lucas", "Andre", "Gustavo", "Renan", "Wallace", "Felipe", "Douglas", "Everton" },
                new[] { "Silva", "Ferreira", "Almeida", "Cardoso", "Barbosa", "Nunes", "Rocha", "Pereira", "Machado", "Duarte", "Sales", "Prado", "Bandeira", "Leal" }),
            ["french"] = (
                new[] { "Antoine", "Julien", "Mathis", "Bastien", "Thierry", "Remy", "Olivier", "Damien", "Luc", "Marcel", "Yannick", "Cedric" },
                new[] { "Laurent", "Dubois", "Moreau", "Girard", "Lefevre", "Marchand", "Perrin", "Chevalier", "Renard", "Bonnet", "Faucher", "Vidal" }),
            ["german"] = (
                new[] { "Lukas", "Stefan", "Matthias", "Jonas", "Florian", "Tobias", "Erik", "Klaus", "Dominik", "Rainer", "Falk", "Timo" },
                new[] { "Fischer", "Kranz", "Weiss", "Roth", "Brandt", "Keller", "Vogel", "Hartmann", "Schuster", "Lange", "Ziegler", "Krause" }),
            ["dutch"] = (
                new[] { "Ruben", "Daan", "Jeroen", "Sven", "Bas", "Thijs", "Wouter", "Koen", "Maarten", "Pim", "Joris", "Rik" },
                new[] { "Vermeer", "Jansen", "Bakker", "Visser", "Smit", "Mulder", "Kuipers", "Hendriks", "Dekker", "Brouwer", "Peters", "Willems" }),
            ["iberian"] = (
                new[] { "Alejandro", "Pablo", "Sergio", "Adrian", "Marcos", "Raul", "Nuno", "Tiago", "Goncalo", "Xavi", "Iker", "Unai" },
                new[] { "Navarro", "Serrano", "Molina", "Iglesias", "Costa", "Marques", "Fonseca", "Cabral", "Herrera", "Pascual", "Reyes", "Ortega" }),
            ["italian"] = (
                new[] { "Matteo", "Luca", "Alessandro", "Marco", "Dario", "Fabio", "Enzo", "Nico", "Emilio", "Renzo", "Paolo", "Salvo" },
                new[] { "Moretti", "Ricci", "Bianchi", "Romano", "Greco", "Conti", "DeLuca", "Ferraro", "Rinaldi", "Caruso", "Villa", "Santoro" }),
            ["scandinavian"] = (
                new[] { "Sven", "Erik", "Magnus", "Oskar", "Henrik", "Lars", "Nils", "Axel", "Bjorn", "Emil", "Viggo", "Anders" },
                new[] { "Bergstrom", "Lindqvist", "Dahl", "Vestergaard", "Nystrom", "Holm", "Eriksen", "Sandvik", "Karlsson", "Fredriksen", "Ahonen", "Virtanen" }),
            ["polish"] = (
                new[] { "Marek", "Piotr", "Tomasz", "Krzysztof", "Bartosz", "Rafal", "Dawid", "Kamil", "Mateusz", "Szymon", "Adrian", "Damian" },
                new[] { "Kowalski", "Nowak", "Wisniewski", "Zielinski", "Szymanski", "Wozniak", "Mazur", "Jablonski", "Krawczyk", "Piotrowski", "Grabowski", "Kaczmarek" }),
            ["czech"] = (
                new[] { "Jakub", "Ondrej", "Petr", "Tomas", "Milan", "Radek", "Vaclav", "Filip", "Roman", "Ludek", "Karel", "Zdenek" },
                new[] { "Novak", "Svoboda", "Dvorak", "Cerny", "Prochazka", "Kucera", "Vesely", "Horak", "Pokorny", "Kral", "Benes", "Ruzicka" }),
            ["hungarian"] = (
                new[] { "Bence", "Adam", "Gergo", "Tamas", "Zoltan", "Attila", "Laszlo", "Balazs", "Levente", "Marton", "Daniel", "Krisztian" },
                new[] { "Nagy", "Kovacs", "Toth", "Szabo", "Horvath", "Varga", "Kiss", "Molnar", "Nemeth", "Farkas", "Balogh", "Papp" }),
            ["romanian"] = (
                new[] { "Andrei", "Mihai", "Alexandru", "Stefan", "Ionut", "Cristian", "Vlad", "Radu", "Gabriel", "Florin", "Dorin", "Cosmin" },
                new[] { "Popescu", "Ionescu", "Codrea", "Stancu", "Dumitru", "Marinescu", "Stoica", "Dobre", "Nistor", "Ciobanu", "Barbu", "Lungu" }),
            ["balkan"] = (
                new[] { "Ivo", "Stefan", "Marko", "Nikola", "Luka", "Dusan", "Vasil", "Petar", "Bojan", "Zoran", "Dario", "Milan" },
                new[] { "Novak", "Petrov", "Vidic", "Petrovic", "Vukovic", "Jovanovic", "Kovacevic", "Ilic", "Stojanovic", "Babic", "Zoric", "Maric" }),
            ["greek"] = (
                new[] { "Nikos", "Giorgos", "Dimitris", "Kostas", "Vasilis", "Panos", "Stavros", "Thanos", "Elias", "Petros", "Makis", "Aris" },
                new[] { "Papadakis", "Nikolaidis", "Georgiou", "Dimitriou", "Karagiannis", "Stavrou", "Antoniou", "Vlachos", "Economou", "Petridis", "Manolis", "Doukas" }),
            ["russian"] = (
                new[] { "Dimitri", "Sergei", "Aleksander", "Ivan", "Nikolai", "Ruslan", "Vladislav", "Anton", "Maksim", "Artem", "Timur", "Magomed", "Islam", "Umar" },
                new[] { "Petrov", "Orlov", "Volkov", "Sokolov", "Kuznetsov", "Morozov", "Fedorov", "Gadzhiev", "Ismailov", "Bagov", "Tarasov", "Yeremenko" }),
            ["baltic"] = (
                new[] { "Mantas", "Lukas", "Tomas", "Darius", "Karolis", "Marius", "Raimonds", "Janis", "Kristaps", "Marten", "Tanel", "Kaido" },
                new[] { "Kazlauskas", "Petrauskas", "Jankauskas", "Ozols", "Berzins", "Kalnins", "Tamm", "Saar", "Kask", "Urbonas", "Vitols", "Rebane" }),
            ["georgian"] = (
                new[] { "Giorgi", "Levan", "Irakli", "Zurab", "Davit", "Nika", "Merab", "Beka", "Tornike", "Vazha", "Otar", "Guram" },
                new[] { "Tsereteli", "Beridze", "Lomidze", "Gelashvili", "Japaridze", "Maisuradze", "Kiknadze", "Abashidze", "Gogoladze", "Khutsishvili", "Kapanadze", "Machavariani" }),
            ["armenian"] = (
                new[] { "Armen", "Tigran", "Vahan", "Gor", "Hayk", "Narek", "Aram", "Sargis", "Levon", "Karen", "Artur", "Vardan" },
                new[] { "Petrosyan", "Grigoryan", "Sargsyan", "Hakobyan", "Avetisyan", "Karapetyan", "Melkonyan", "Ghazaryan", "Manukyan", "Harutyunyan", "Vardanyan", "Mkrtchyan" }),
            ["turkic"] = (
                new[] { "Emre", "Kerem", "Volkan", "Serkan", "Burak", "Ozan", "Tarkan", "Cem", "Kaan", "Murat", "Elvin", "Rashad" },
                new[] { "Demir", "Yilmaz", "Kaya", "Celik", "Aslan", "Ozturk", "Sahin", "Aliyev", "Mammadov", "Guliyev", "Erdem", "Koc" }),
            ["arabic"] = (
                new[] { "Karim", "Yusuf", "Omar", "Tariq", "Hassan", "Rami", "Ziad", "Nabil", "Fares", "Adnan", "Sami", "Bilal", "Jamal", "Khalid" },
                new[] { "Haddad", "Nasser", "Khalil", "Aziz", "Farouk", "Suleiman", "Darwish", "Baraka", "Zaidi", "Mansour", "Salloum", "Hamdan" }),
            ["persian"] = (
                new[] { "Reza", "Amir", "Behnam", "Farhad", "Kaveh", "Navid", "Pouya", "Sohrab", "Arash", "Babak", "Danial", "Omid" },
                new[] { "Rahimi", "Karimi", "Jafari", "Moradi", "Ahmadi", "Ebrahimi", "Sadeghi", "Ghasemi", "Nazari", "Sharifi", "Kazemi", "Fallah" }),
            ["israeli"] = (
                new[] { "Noam", "Eitan", "Yoav", "Amit", "Oren", "Tal", "Ido", "Nadav", "Asaf", "Lior", "Gilad", "Ronen" },
                new[] { "Barak", "Mizrahi", "Peretz", "Dahan", "Azoulay", "Malka", "Gabay", "Shaked", "Harel", "Golan", "Navon", "Zohar" }),
            ["japanese"] = (
                new[] { "Kaito", "Haruki", "Sota", "Takumi", "Daichi", "Hiro", "Yuto", "Kenta", "Ryo", "Shinji", "Kazuki", "Renji" },
                new[] { "Tanaka", "Watanabe", "Nakamura", "Ito", "Yoshida", "Okada", "Kubo", "Morita", "Hattori", "Fujimoto", "Kimura", "Sakurai" }),
            ["korean"] = (
                new[] { "Minho", "Jiho", "Jaewon", "Donghyun", "Sungmin", "Taeyang", "Hyunwoo", "Joon", "Kyungho", "Sangwook", "Yongjun", "Chansung" },
                new[] { "Kim", "Park", "Lee", "Choi", "Jung", "Seo", "Kang", "Yoon", "Lim", "Han", "Shin", "Kwon" }),
            ["chinese"] = (
                new[] { "Wei", "Jin", "Bao", "Feng", "Hao", "Cheng", "Lei", "Ming", "Tao", "Xiang", "Zhen", "Kang" },
                new[] { "Zhang", "Wang", "Chen", "Lin", "Liu", "Huang", "Zhao", "Wu", "Zhou", "Xu", "Sun", "Ma" }),
            ["mongolian"] = (
                new[] { "Bataar", "Ganbold", "Temuulen", "Naran", "Chuluun", "Erdene", "Sukhbat", "Altan", "Bold", "Munkh", "Tomor", "Zorig" },
                new[] { "Batbayar", "Erdenebat", "Tumur", "Ochir", "Altankhuyag", "Munkhbat", "Dorj", "Enkhbold", "Baterdene", "Chinbat", "Gantulga", "Purev" }),
            ["thai"] = (
                new[] { "Somchai", "Anucha", "Kittipong", "Prasit", "Somsak", "Chalerm", "Narong", "Thanet", "Wichai", "Decha", "Sarawut", "Kritsada" },
                new[] { "Rattanak", "Chaowalit", "Kaewkanya", "Sorndech", "Phromsri", "Klahan", "Chaidet", "Sakda", "Thongchai", "Boonmee", "Srisawat", "Panya" }),
            ["vietnamese"] = (
                new[] { "Minh", "Duc", "Hung", "Quang", "Tuan", "Long", "Khoa", "Phong", "Bao", "Trung", "Hieu", "Nam" },
                new[] { "Nguyen", "Tran", "Le", "Pham", "Hoang", "Vu", "Dang", "Bui", "Do", "Ho", "Ngo", "Duong" }),
            ["filipino"] = (
                new[] { "Joshua", "Marlon", "Carlo", "Jerome", "Ryan", "Alvin", "Dante", "Emil", "Paolo", "Rico", "Jayson", "Noel" },
                new[] { "Santos", "Reyes", "Cruz", "Bautista", "Villanueva", "Aquino", "Ramos", "Mendoza", "Torres", "Flores", "Pacleb", "Ocampo" }),
            ["indonesian"] = (
                new[] { "Rizal", "Agus", "Budi", "Dedi", "Eko", "Fajar", "Hadi", "Irfan", "Joko", "Rudi", "Slamet", "Wawan" },
                new[] { "Putra", "Santoso", "Wijaya", "Susanto", "Halim", "Gunawan", "Setiawan", "Kurniawan", "Saputra", "Hidayat", "Pratama", "Utama" }),
            ["indian"] = (
                new[] { "Arjun", "Ravi", "Rohan", "Sanjay", "Vikram", "Amit", "Rahul", "Karan", "Nikhil", "Aditya", "Vijay", "Suresh" },
                new[] { "Sharma", "Reddy", "Gupta", "Rao", "Malhotra", "Singh", "Patel", "Kumar", "Mehta", "Chopra", "Nair", "Verma" }),
            ["centralasian"] = (
                new[] { "Arman", "Timur", "Daniyar", "Nursultan", "Bekzat", "Aibek", "Ruslan", "Erlan", "Sanzhar", "Kairat", "Azamat", "Yerlan" },
                new[] { "Batyr", "Zhaksylyk", "Nurlanov", "Abdullaev", "Toktarov", "Serikov", "Amanov", "Bekov", "Umarov", "Rakhimov", "Sultanov", "Dzhanibekov" }),
            ["westafrican"] = (
                new[] { "Kwame", "Tunde", "Chidi", "Musa", "Kofi", "Femi", "Jabari", "Idris", "Obi", "Sekou", "Mamadou", "Yaw" },
                new[] { "Okonkwo", "Adeyemi", "Mensah", "Diallo", "Achebe", "Osei", "Njoku", "Bello", "Owusu", "Okafor", "Toure", "Ndiaye" }),
            ["eastafrican"] = (
                new[] { "Baraka", "Juma", "Amani", "Jelani", "Kito", "Zuberi", "Rashidi", "Bakari", "Hamisi", "Salim", "Omari", "Abel" },
                new[] { "Kamau", "Otieno", "Mwangi", "Kiprop", "Wanjala", "Ochieng", "Njoroge", "Mutua", "Korir", "Barasa", "Odhiambo", "Maina" }),
            ["centralafrican"] = (
                new[] { "Dieudonne", "Patrice", "Emery", "Blaise", "Serge", "Junior", "Glody", "Tresor", "Fiston", "Pacome", "Herve", "Christian" },
                new[] { "Mabiala", "Kalonji", "Ilunga", "Kabongo", "Mukendi", "Tshimanga", "Bokamba", "Ngoy", "Mbemba", "Kasongo", "Lumbala", "Bofane" }),
            ["southernafrican"] = (
                new[] { "Sipho", "Bongani", "Themba", "Zane", "Tendai", "Lwazi", "Kagiso", "Thabo", "Sizwe", "Mandla", "Neo", "Katlego" },
                new[] { "Dlamini", "Ndlovu", "Mabaso", "Sithole", "Moyo", "Khumalo", "Nkosi", "Zulu", "Mokoena", "Botha", "VanWyk", "Pretorius" }),
            ["polynesian"] = (
                new[] { "Tane", "Hemi", "Manaia", "Nikau", "Sione", "Koa", "Tavita", "Manu", "Rangi", "Ariki", "Sefo", "Lopeti" },
                new[] { "Whitiora", "Faga", "Mafi", "Tui", "Ngata", "Vuni", "Rahiri", "Palepoi", "Fetu", "Havili", "Taufa", "Kaufusi" }),
        };

    private static readonly IReadOnlyDictionary<Region, (IReadOnlyList<string> First, IReadOnlyList<string> Last)> Pools =
        new Dictionary<Region, (IReadOnlyList<string>, IReadOnlyList<string>)>
        {
            [Region.NorthAmerica] = (
                new[] { "Cody", "Tyrell", "Marcus", "Jalen", "Brody", "Devin", "Trent", "Shane", "Dominic", "Kade", "Rashad", "Colton", "Miles", "Darnell", "Wyatt", "Chance", "Isaiah", "Grant" },
                new[] { "Harker", "Boone", "Whitlock", "Sanders", "Delgado", "Cross", "Maddox", "Reyes", "Vance", "Holt", "Briggs", "Okafor", "Nash", "Sable", "Dunn", "Ferris" }),
            [Region.SouthAmerica] = (
                new[] { "Rafael", "Thiago", "Vinicius", "Mateus", "Caio", "Diego", "Bruno", "Emiliano", "Rodrigo", "Lucas", "Andre", "Gustavo", "Joaquin", "Nahuel", "Facundo", "Renan" },
                new[] { "Silva", "Ferreira", "Almeida", "Cardoso", "Barbosa", "Nunes", "Rocha", "Moreno", "Vargas", "Quintero", "Pereira", "Sosa", "Machado", "Duarte", "Bianchi" }),
            [Region.Europe] = (
                new[] { "Lukas", "Mateo", "Sven", "Aleksander", "Dimitri", "Finn", "Marek", "Niall", "Ivo", "Ruben", "Anton", "Cillian", "Gethin", "Stefan", "Vasil", "Oskar" },
                new[] { "Novak", "Fischer", "Laurent", "Kowalski", "Bergstrom", "Moretti", "Petrov", "OShea", "Vermeer", "Kranz", "Halloran", "Vidic", "Dubois", "Lindqvist", "Ricci" }),
            [Region.Asia] = (
                new[] { "Kaito", "Jin", "Wei", "Haruki", "Minho", "Arjun", "Ravi", "Sota", "Takumi", "Daichi", "Rohan", "Nguyen", "Hiro", "Sanjay", "Bao", "Yuto" },
                new[] { "Tanaka", "Kim", "Zhang", "Watanabe", "Park", "Sharma", "Nakamura", "Reddy", "Wang", "Ito", "Chen", "Gupta", "Yoshida", "Lin", "Rao", "Okada" }),
            [Region.Oceania] = (
                new[] { "Jai", "Kai", "Tane", "Beau", "Hemi", "Lachlan", "Manaia", "Tyson", "Nikau", "Riley", "Marlon", "Ari", "Sione", "Koa" },
                new[] { "Whitiora", "Faga", "Mafi", "Harrow", "Tui", "Blackwood", "Ngata", "Vuni", "Cross", "Rahiri", "Palepoi", "Marsh" }),
            [Region.Africa] = (
                new[] { "Kwame", "Sipho", "Tunde", "Chidi", "Amara", "Musa", "Kofi", "Zane", "Bongani", "Femi", "Jabari", "Idris", "Themba", "Obi", "Rashid" },
                new[] { "Okonkwo", "Dlamini", "Adeyemi", "Mensah", "Kamau", "Ndlovu", "Diallo", "Achebe", "Osei", "Mabaso", "Njoku", "Sithole", "Bello", "Owusu" }),
            [Region.MiddleEast] = (
                new[] { "Karim", "Yusuf", "Omar", "Tariq", "Hassan", "Rami", "Ziad", "Nabil", "Fares", "Adnan", "Sami", "Bilal", "Jamal", "Reza" },
                new[] { "Haddad", "Nasser", "Khalil", "Aziz", "Farouk", "Rahimi", "Suleiman", "Darwish", "Baraka", "Zaidi", "Mansour", "Karimi", "Salloum" }),
        };

    // --- Nicknames -----------------------------------------------------------
    // A fight name should sound like it was shouted by the fighter's own crowd,
    // so nicknames key off the same name CULTURE as the name itself: a Brazilian
    // is "O Predador", a Georgian is "The Bear of Kakheti", an Osaka kid is
    // "Little Dragon". Beneath the authored lists sits a combinatorial tail
    // ("Iron Wolf", "The Sledgehammer") deep enough that a world of thousands
    // doesn't end up with forty men called The Hammer — the generator escalates
    // into it when the authored picks are already spoken for.

    /// <summary>Which nickname pool a name culture draws from (the 39 cultures roll up into 13 flavours).</summary>
    private static string NicknameFamily(string? culture) => culture switch
    {
        "brazilian" => "brazilian",
        "latino" or "iberian" => "latino",
        "french" or "italian" or "greek" => "romance",
        "german" or "dutch" or "scandinavian" => "germanic",
        "russian" or "polish" or "czech" or "balkan" or "baltic" or "romanian" or "hungarian"
            or "georgian" or "armenian" => "slavic",
        "arabic" or "persian" or "israeli" or "turkic" => "mideast",
        "japanese" or "korean" or "chinese" or "vietnamese" => "eastasia",
        "thai" or "indonesian" or "filipino" => "seasia",
        "mongolian" or "centralasian" => "steppe",
        "indian" => "indian",
        "westafrican" or "centralafrican" or "eastafrican" or "southernafrican" => "african",
        "polynesian" => "pacific",
        _ => "anglo",
    };

    /// <summary>The continental fallback when a fighter has no name culture (older callers).</summary>
    private static string NicknameFamily(Region region) => region switch
    {
        Region.SouthAmerica => "latino",
        Region.Europe => "slavic",
        Region.Asia => "eastasia",
        Region.Africa => "african",
        Region.MiddleEast => "mideast",
        Region.Oceania => "pacific",
        _ => "anglo",
    };

    /// <summary>
    /// Roll a fight name from a SINGLE random draw — pool choice and index both
    /// come out of the one 64-bit value, so generating a nickname costs the
    /// generator exactly what <c>rng.Pick</c> used to and every seeded world
    /// keeps the shape it was balanced with.
    /// </summary>
    public static string RollNickname(ulong roll, string? culture, Region region, Sex sex)
    {
        string family = culture is not null ? NicknameFamily(culture) : NicknameFamily(region);

        // Mostly the fighter's own flavour, with a real slice of the shared
        // fight-game vocabulary so nobody's nationality is a straitjacket.
        var pool = sex == Sex.Female && roll % 100 < 35 ? FemaleNicknames
                 : (roll >> 8) % 100 < 72 && NicknamesByFamily.TryGetValue(family, out var fam) ? fam
                 : SharedNicknames;
        return pool[(int)((roll >> 16) % (ulong)pool.Count)];
    }

    /// <summary>
    /// The RE-ROLL used when the world already has somebody by that name.
    /// <paramref name="attempt"/> climbs with each collision: the first tries stay
    /// in the authored pools, later ones drop into the combinatorial tail, which
    /// is deep enough never to run dry however big the sport gets.
    /// </summary>
    public static string RollNickname(SimRandom rng, string? culture, Region region, Sex sex, int attempt = 0)
    {
        if (attempt < 3) return RollNickname(rng.NextULong(), culture, region, sex);

        // The tail. Four shapes over the word lists give ~6,000 distinct fight
        // names — comfortably more than the whole sport, retired filler included,
        // so the re-roll always finds an unused one within a try or two. Only the
        // ANIMAL/WEATHER/TOOL nouns take an adjective; the wry occupational ones
        // ("the Debt Collector") only ever stand alone, because "The Bitter
        // Handbrake" is not a nickname anybody has ever been given.
        return rng.Range(0, 3) switch
        {
            0 => $"The {rng.Pick(NicknameSolo)}",
            1 => $"{rng.Pick(NicknameAdjectives)} {rng.Pick(NicknameNouns)}",
            2 => $"The {rng.Pick(NicknameAdjectives)} {rng.Pick(NicknameNouns)}",
            _ => $"The {rng.Pick(NicknameNouns)} of {rng.Pick(NicknameOf)}",
        };
    }

    private static readonly IReadOnlyList<string> SharedNicknames = new[]
    {
        "The Hammer", "Cold Steel", "The Surgeon", "Nightmare", "The Phantom", "Bad Intentions",
        "The Anvil", "The Reaper", "Thunder", "The Technician", "Wolfpack",
        "The Sandman", "Iron", "The Prodigy", "Ruthless", "The Viper", "Blackout",
        "The Machine", "Smooth", "The General", "Diesel", "The Assassin", "The Kid",
        "Warhorse", "The Butcher", "Lightning", "The Menace", "Stone Hands", "The Wizard",
        "One Shot", "The Problem", "Last Call", "The Lantern Jaw", "Bad Traffic", "The Payment",
        "Overtime", "The Quiet Man", "Nine Lives", "The Long Night", "Doctor Stoppage",
        "The Understudy", "House Money", "The Closer", "Bare Minimum", "The Tourist",
    };

    // Female-leaning fight names — women draw from these as well as the neutral pools.
    private static readonly IReadOnlyList<string> FemaleNicknames = new[]
    {
        "The Tigress", "Wildcat", "The Duchess", "Nightshade", "Little Trouble", "The Vixen",
        "Red Queen", "The Empress", "Hurricane", "The Firebird", "Bad Manners", "Sledgehammer",
        "The Huntress", "Poison Ivy", "The Lioness", "Riot Girl", "The Warden", "Steel Rose",
        "Ice Queen", "The Banshee", "Trouble", "The Widowmaker",
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NicknamesByFamily =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["anglo"] = new[]
            {
                "The Natural", "Hollywood", "Sugar", "The Boogeyman", "Bulldog", "Money",
                "The Underdog", "Cowboy", "Highlight", "The Franchise", "Bad Company", "Lights Out",
                "The Deacon", "Buckshot", "Southpaw Sam", "The Alarm Clock", "Country Strong",
                "The Nightcap", "Homework", "Sunday Punch", "The Handyman", "Free Lunch",
            },
            ["latino"] = new[]
            {
                "El Toro", "La Sombra", "El Martillo", "El Lobo", "La Bestia", "El Fantasma",
                "El Cirujano", "La Roca", "El Rayo", "El Halcón", "La Tormenta", "El Verdugo",
                "Aguja", "El Chacal", "La Furia", "El Rey", "Cabeza Dura", "El Terremoto",
                "La Pantera", "El Nudillo", "Corazón", "El Tren",
            },
            ["brazilian"] = new[]
            {
                "Pitbull", "O Predador", "Cavalo", "Trator", "O Tubarão", "Furacão",
                "Pedreiro", "O Feiticeiro", "Correnteza", "Bagre", "O Carrasco", "Minotauro",
                "Serrote", "O Ferro", "Zumbi", "Passarinho", "O Dentista", "Marreta",
                "Capitão", "O Silencioso", "Rocha", "Formiga",
            },
            ["romance"] = new[]
            {
                "Le Boucher", "Il Lupo", "Le Fantôme", "Il Martello", "L'Horloger", "Il Chirurgo",
                "Le Requin", "La Bestia", "Le Chat", "Il Fabbro", "Le Cyclone", "Il Muro",
                "Le Comte", "O Leão", "Le Facteur", "Il Professore", "Le Bûcheron", "Kyklonas",
                "Le Métronome", "Il Silenzio",
            },
            ["germanic"] = new[]
            {
                "Der Hammer", "De Beul", "Der Wolf", "IJzeren", "Der Chirurg", "De Slager",
                "Vikingen", "Der Turm", "De Machine", "Isbjørn", "Der Uhrmacher", "Nordlys",
                "De Storm", "Der Fels", "Blixt", "Der Vollstrecker", "De Kraan", "Järn",
                "Der Kaltblüter", "Bergtroll",
            },
            ["slavic"] = new[]
            {
                "The Bear", "Krotky", "The Tsar", "Vuk", "The Sledge", "Medved",
                "The Sickle", "Kamen", "The Winter", "Kovach", "The Iron Curtain", "Grom",
                "The Bricklayer", "Zima", "The Executioner", "Sokol", "The Cold Room",
                "Ozols", "The Long Knife", "Vityaz", "The Quiet Cousin", "Chugun",
            },
            ["mideast"] = new[]
            {
                "The Falcon", "Al-Asad", "The Sandstorm", "Sayf", "The Cedar", "Al-Jabal",
                "The Scorpion", "Kartal", "The Caravan", "Al-Nimr", "The Dune", "Şahin",
                "The Lantern", "Al-Sakhr", "The Desert Wind", "Pahlavan", "The Oasis",
                "Demir", "The Gatekeeper", "Zulfiqar",
            },
            ["eastasia"] = new[]
            {
                "Little Dragon", "The Silent One", "Kaminari", "Iron Monk", "The Crane",
                "Tetsu", "The Mantis", "Baekho", "The Typhoon", "Kuroi", "The Sparrow",
                "Hanma", "The Bamboo", "Ryu", "The Winter Plum", "Jeoldo", "The Anvil of Osaka",
                "Kirin", "The Paper Lantern", "Shin",
            },
            ["seasia"] = new[]
            {
                "The Elephant", "Sua", "Thunderfoot", "The Golden Boy", "Naga", "The Temple Dog",
                "Harimau", "The River", "Petch", "The Monsoon", "Garuda", "The Bamboo Knife",
                "Singh Dam", "The Ferryman", "Macan", "The Left Shin", "Kraithong",
                "The Night Market", "Rimau", "The Long Boat",
            },
            ["steppe"] = new[]
            {
                "Iron Horse", "Berkut", "The Steppe Wolf", "Batyr", "The Khan", "Arslan",
                "The Cold Wind", "Bars", "The Wrestler's Son", "Temir", "The Long Ride",
                "Bürte", "The Anvil", "Kokjal", "The Horseman", "Zhelezo", "The Yurt",
                "Tulpar", "The Dust Cloud", "Kuresh",
            },
            ["indian"] = new[]
            {
                "The Tiger", "Sher", "The Mumbai Mauler", "Bajrang", "The Cobra", "Pehlwan",
                "The Monsoon", "Vajra", "The Akhara Kid", "Chakra", "The Elephant God",
                "Bandar", "The Long Arm", "Garuda", "The Iron Bangle", "Sultan",
                "The Night Train", "Kabaddi", "The Stone Lion", "Hukum",
            },
            ["african"] = new[]
            {
                "The Lion", "Simba", "Table Mountain", "The Rhino", "Chui", "The Baobab",
                "Ngwenya", "The Harmattan", "Mamba", "The Drum", "Impi", "The Lagos Lightning",
                "Tembo", "The Kalahari", "Nyati", "The Copper Fist", "Zulu Time",
                "Fisi", "The Long Walk", "Kudu",
            },
            ["pacific"] = new[]
            {
                "The Reef", "Tsunami", "The Tide", "Mana", "The Islander", "Storm Front",
                "The Ocean", "Toa", "The Coconut Wireless", "Riptide", "The Big Wave",
                "Tapu", "The Southerly", "Moana", "The Breakwater", "Kaha",
                "The Trade Wind", "Vaka", "The Deep", "Whetu",
            },
        };

    // The combinatorial tail — 48 adjectives × 72 nouns over four shapes is a
    // little over nine thousand fight names, so the world never runs out however
    // deep the sport gets (a built world carries ~2,500 nicknamed fighters once
    // the back-dated opposition is counted).
    private static readonly IReadOnlyList<string> NicknameAdjectives = new[]
    {
        "Iron", "Cold", "Black", "Silent", "Mad", "Golden", "Wild", "Steel", "Red", "Lone",
        "Quiet", "Savage", "Bad", "Stone", "Midnight", "Rolling", "Crooked", "Grim", "Blue",
        "Hungry", "Broken", "Rusty", "Sharp", "Heavy", "Low", "Long", "Bitter", "Sudden",
        "Patient", "Ragged", "Loud", "Bone", "Winter", "Thunder", "Copper", "Northern",
        "Hard", "Slow", "Ruthless", "Feral", "Ancient", "Restless", "Honest", "Crooked-Nose",
        "Barefoot", "Sleepless", "Unpaid", "Borrowed",
    };

    // Nouns that take an adjective and still sound like a fighter.
    private static readonly IReadOnlyList<string> NicknameNouns = new[]
    {
        "Wolf", "Hammer", "Anvil", "Bull", "Shark", "Hawk", "Bear", "Crow", "Snake", "Fox",
        "Mule", "Boar", "Falcon", "Jackal", "Panther", "Ram", "Hound", "Owl", "Cobra", "Badger",
        "Magpie", "Heron", "Stallion", "Wasp", "Mongoose", "Vulture", "Lynx", "Rooster",
        "Sledgehammer", "Engine", "Furnace", "Hatchet", "Scythe", "Wrench", "Bell", "Nail",
        "Vice", "Crowbar", "Piston", "Chisel",
        "Storm", "Tide", "Frost", "Avalanche", "Undertow", "Drought", "Squall", "Landslide",
        "Riptide", "Blizzard", "Sinkhole", "Downpour",
    };

    // The wry ones — only ever "The {x}", never with an adjective in front.
    private static readonly IReadOnlyList<string> NicknameSolo = new[]
    {
        "Professor", "Undertaker", "Mechanic", "Butcher", "Bricklayer", "Nightwatchman",
        "Debt Collector", "Bad Neighbour", "Landlord", "Ferryman", "Bailiff", "Understudy",
        "Verdict", "Sentence", "Contract", "Invoice", "Curfew", "Rent", "Ledger", "Turnstile",
        "Handbrake", "Lamppost", "Last Word", "Closing Argument", "Overdraft", "Deadline",
    };

    // …of somewhere, or of something. The fourth shape.
    private static readonly IReadOnlyList<string> NicknameOf = new[]
    {
        "Winter", "the North", "the Docks", "Sunday", "the Long Night", "the Old Road",
        "the Border", "Last Orders", "the Quarry", "the Night Shift", "the Cheap Seats",
        "the Back Room", "the Coast", "the Valley", "the Iron Works", "Closing Time",
        "the Low Country", "the Ninth Round", "the Empty Gym", "the Second Chance",
        "the Wrong Side", "the Long Drive", "the Late Bus", "the Hard Way",
    };
}
