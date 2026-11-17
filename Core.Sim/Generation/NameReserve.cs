namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// The reserve: extra first names and surnames per name culture that the ordinary name roll NEVER draws
/// from (so every existing roll is untouched). Only a fighter whose rolled name is already taken, and who
/// could not be settled by the original re-roll ladder, reaches them (see
/// <see cref="FighterGenerator.EnsureUniqueName(MmaManager.Core.Model.World, MmaManager.Core.Model.Fighter, bool)"/>).
/// They exist because the base pools are small (a dozen of each) and a world holds thousands of people:
/// each culture's base plus reserve is sized so first x surname is at least twice the people projected
/// for that culture at year 25 of a career (retired fighters stay on the books), leaving the hyphenated
/// and numbered fall-backs as last resorts that should never fire.
///
/// Rules: real-sounding common names for the culture, ASCII like the base pools, nothing that is a
/// designed fighter's, legend's, pioneer's, staffer's or owner's surname (a generated "Volkov" who is not
/// family reads as family), no distinctive surname of a real fighter or celebrity, and nothing repeated
/// inside a culture's base pool (a test enforces all of it). Women draw from the culture's surnames too, so
/// surnames here serve both sexes. Append-only: the order of a list feeds the draw.
/// </summary>
internal static class NameReserve
{
    private static string[] W(string names) => names.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static (IReadOnlyList<string> First, IReadOnlyList<string> Last) For(string? culture) =>
        culture is not null && Cultures.TryGetValue(culture, out var r) ? r : (Array.Empty<string>(), Array.Empty<string>());

    internal static IReadOnlyDictionary<string, (IReadOnlyList<string> First, IReadOnlyList<string> Last)> All => Cultures;

    private static readonly IReadOnlyDictionary<string, (IReadOnlyList<string> First, IReadOnlyList<string> Last)> Cultures =
        new Dictionary<string, (IReadOnlyList<string>, IReadOnlyList<string>)>(StringComparer.Ordinal)
        {
            ["anglo"] = (
                W("Landon Hunter Tucker Gavin Preston Beau Reid Caleb Jaxon Marshall Dallas Cooper Tanner Garrett Spencer Brandon Austin Travis Lance Dwayne Terrence Malik Xavier Elijah Jordan Cedric Derek Reggie Calvin Warren Clint Randall Heath Boyd Rhett Ryder Gunnar Jace Eli Silas Callum Dexter Franklin Lamar Carlton Nathan Quentin Desmond Everett Walker Brock Stuart Rory Beckett Lyle Mitchell Hayden"),
                W("Garrity Wyman Brannigan Hollis Winslow Shepard Calloway Bishop Garner Lockhart Thornton Ellison Hargrove Blevins Pickett Sutter Merritt Dawson Rayburn Carver Lowell Prescott Ackerman Burnett Crowley Dalton Easley Fenton Gentry Hatcher Ingram Jarrett Kirby Lambert Marlow Odell Quillen Rutledge Spooner Tolliver Underhill Vickers Wexler Wardell Zimmer Abbott Barlow Coburn Denton Emory Fairchild Gilmore Haskell Irvin Locke Mattox Nolte Rawlings Sheridan Tennant")),
            ["arabic"] = (
                W("Idris Walid Majid Samir Faisal Imad Hatem Ghassan Marwan Nizar Rashid Salim Tamer Wael Zaki Anwar Basil Elias Fadi Hadi Ibrahim Louay Mazen Nadim Qasim Raed Sharif Taha Yazan Zaid Amir Dawud"),
                W("Habib Jaber Kassab Maalouf Najjar Shami Rizk Saad Tawil Yassin Zahra Abboud Bishara Daher Essa Fakhoury Ghanem Hamad Jarrah Karam Lahham Masri Nakhle Obeid Rahal Sabbagh Touma Wehbe Younes Zeidan Atallah Barakat")),
            ["balkan"] = (
                W("Goran Darko Aleksa Nemanja Dragan Mladen Vuk Ognjen"),
                W("Stankovic Markovic Nikolic Djordjevic Pavlovic Todorovic Radovic Lazic Simic Matic")),
            ["brazilian"] = (
                W("Joao Pedro Gabriel Matheus Leonardo Fabricio Murilo Otavio Henrique Igor Jonas Kleber Luciano Marcio Nelson Paulo Ricardo Sandro Vitor Wesley Yuri Alex Breno Claudio Davi Edson Fernando Geraldo Hugo Iuri Jefferson Lincoln Marlon Nilton Osvaldo Reginaldo Samuel Tulio Ulisses"),
                W("Bezerra Santos Ribeiro Cordeiro Gomes Martins Araujo Melo Teixeira Moura Campos Dias Freitas Lopes Medeiros Nogueira Pinto Ramos Louzada Tavares Vasconcelos Xavier Azevedo Batista Cavalcanti Drummond Fagundes Guimaraes Lacerda Macedo Brandao Peixoto Queiroz Rezende Salgado Toledo Viana Zanetti Albuquerque Borges Coelho Esteves Furtado")),
            ["celtic"] = (
                W("Brendan Colm Tadhg Kieran Eamon"),
                W("Callaghan Fitzgerald Sheehan Lenihan Gilroy Kavanagh Tierney Cassidy Mulvaney")),
            ["centralafrican"] = (
                W("Aristide Landry Patient Prince Gaston"),
                W("Mbuyi Kitenge Mulumba Lusamba Nzuzi Kanyinda")),
            ["centralasian"] = (
                W("Nurlan Dastan Marat Almaz Baurzhan Berik Dauren Galym Kanat Medet Nurzhan Rinat Talgat Zhanibek Asylbek Eldar Farhod Jasur Sardor Bakhtiyor"),
                W("Karimov Nazarov Ospanov Satybaldiev Tursunov Yusupov Kasymov Zhunusov Omarov Iskakov Mukhametov Nurgaliev Rustamov Saidov Zakirov Abdrakhmanov Mamytov Dosmukhambetov Tashkenbaev Kenzhebaev")),
            ["chinese"] = (
                W("Jun Yong Gang Qiang Bo Hui Peng Yu Zhi Dong Rong"),
                W("Yang Zhu Hu Guo He Gao Luo Zheng Liang Xie Tang Cao Feng Deng")),
            ["dutch"] = (
                W("Lars Gijs Niels Stijn Jasper Floris"),
                W("Hoekstra Vandenberg Dijkstra Bos Kramer Prins Meijer DeGroot")),
            ["eastafrican"] = (
                W("Wekesa Mugisha Okello Tadesse Biruk Abdi Daudi Faraji"),
                W("Cheruiyot Kipkemboi Akello Tumwine Wafula Opiyo Kyalo Gebre Tesfaye")),
            ["filipino"] = (
                W("Aldrin"),
                W("Dizon Lacson Manalo Soriano")),
            ["french"] = (
                W("Hugo Maxime Etienne Gaspard Lucien Romain Baptiste Fabrice"),
                W("Fontaine Garnier Roussel Berger Lemaire Colin Masson Rousseau")),
            ["georgian"] = (
                W("Lasha Gela Soso Temur Shota Badri Nodar Revaz"),
                W("Mgaloblishvili Chkheidze Dolidze Gvaramia Jincharadze Tavadze Bakradze Chikovani Kobakhidze")),
            ["german"] = (
                W("Jan Philipp Sebastian Moritz Leon Niklas Jannik Hendrik Gerrit Ulf Konrad"),
                W("Schneider Wagner Becker Hoffmann Bauer Richter Koch Neumann Schwarz Zimmermann Braun Hahn Lorenz")),
            ["iberian"] = (
                W("Jordi Oriol Aitor Hugo Ruben"),
                W("Aguado Beltran Cortes Domingo Esteban Gimeno Lacalle Sanchis Moya Bermudez")),
            ["indian"] = (
                W("Anil Deepak Gaurav Harsh Kunal Manish Naveen Pranav Rajesh Tarun Yash Dev"),
                W("Iyer Joshi Menon Pillai Banerjee Desai Bhatt Chaudhary Das Ghosh Naidu Trivedi")),
            ["indonesian"] = (
                W("Bayu Gilang Ilham Teguh"),
                W("Nugroho Hartono Wibowo Siregar")),
            ["israeli"] = (
                W("Ariel Dror Guy Itai Shlomi Yaniv"),
                W("Levi Katz Amar Biton Cohen Avraham")),
            ["japanese"] = (
                W("Ren Haruto Yamato Sora Riku Taiga Kosei Makoto Naoki Satoshi Tomoya Yusei Akira Goro Isamu Jun Koji Masato"),
                W("Mizuno Inoue Shimizu Saito Hayashi Ishikawa Maeda Fujita Ogawa Goto Hasegawa Murata Ishii Sakamoto Endo Fukuda Nomura Sugiyama Yamaguchi Kobayashi")),
            ["korean"] = (
                W("Seungho Dongwook Jinwoo Hyunjun Woojin Taehyun Sanghoon Youngho Junseo"),
                W("Jeon Hwang Song Gwak Moon Noh Bae Hong Yang")),
            ["latino"] = (
                W("Sebastian Matias Leandro Gonzalo Ramiro Tomas Esteban Fernando Gabriel Julian Maximiliano Bautista Lautaro Federico Ezequiel Agustin Enrique Nicolas Pablo Ricardo Armando Hector Gustavo Eduardo Mauricio Cesar Ernesto Ulises Orlando Rolando Wilmer Jorge Luis Manuel Oscar Pedro Raul Samuel Victor Adrian Andres Camilo Dario Felipe Guillermo Humberto Jairo Leonel Marcelo"),
                W("Aguirre Bustos Cabrera Davila Escobar Figueroa Galindo Guzman Juarez Lozano Maldonado Navas Palacios Barrientos Villalobos Soto Tapia Urrutia Valdez Zamora Acosta Benitez Castillo Espinoza Franco Gallegos Huerta Jimenez Lara Mejia Naranjo Olivares Pacheco Quesada Robles Sandoval Trujillo Uribe Velasco Yanez Arce Bravo Contreras Duran Estrada Gamboa Hidalgo Ledesma")),
            ["mongolian"] = (
                W("Ariunbold Tsogt"),
                W("Lkhagva Davaa")),
            ["persian"] = (
                W("Kian Mehdi Shahin Siavash Bahram Dariush Ehsan Hamed Kourosh"),
                W("Hosseini Mahdavi Rezaei Tehrani Zand Abbasi Bahrami Daneshvar Farahani Najafi")),
            ["polish"] = (
                W("Lukasz Michal Pawel Wojciech Grzegorz Robert"),
                W("Wojcik Kaminski Dabrowski Kozlowski Jankowski Wojciechowski Kwiatkowski Kubiak Michalski")),
            ["polynesian"] = (
                W("Tama Wiremu Hone Rawiri Mika Kahu Tiaki Eruera Siaosi Viliami Tevita Ilaisa Isaia Taini Kauri Atonio Fonoti Malo Pita Rua Whetu Maka Kimo Lomano Nuku"),
                W("Matagi Latu Tupou Vaea Aumua Leota Tago Siale Ioane Pouono Wihongi Parata Rewi Tawhiri Kereama Pomare Hapi Talia Vea Tongia Heke Taiaroa Tamihana Moala Lolohea")),
            ["romanian"] = (
                W("Sorin"),
                W("Tudor Constantin Munteanu Georgescu")),
            ["russian"] = (
                W("Boris Gleb Denis Yuri Oleg Pavel Roman Stanislav Valery Viktor Yaroslav Kirill Leonid Grigory Mikhail Konstantin Andrei Igor Arkady Zakhar Eduard Rustam Said Abdul"),
                W("Smirnov Popov Lebedev Kozlov Novikov Pavlov Semenov Golubev Vinogradov Belov Komarov Zaitsev Frolov Gusev Kiselev Mikhailov Nikitin Titov Chernov Sorokin Yakovlev Rodionov Borisov Dementiev Ermakov Filatov Grishin Isaev Larin Maslov Zubov")),
            ["scandinavian"] = (
                W("Leif Gustav Torsten Mikael Rasmus Soren Ulrik Kristian Mattias"),
                W("Johansson Larsen Haugen Pedersen Engberg Strand Aaltonen Koskinen Hedlund Ostergaard Berglund")),
            ["southernafrican"] = (
                W("Lungile Siyabonga Tshepo Vusi Xolani Andile Blessing Farai Tafara Tinashe Nhlanhla Mpho Dumisani Jabu Lindani Njabulo Oupa Pieter Riaan Wian Johan"),
                W("Mthembu Cele Radebe Shabalala Zwane Mahlangu Ngcobo Masuku Tshabalala Molefe Ncube Sibanda Chikwanha Mutasa Venter Joubert Coetzee DuPlessis Naidoo Nel Breytenbach Oosthuizen")),
            ["thai"] = (
                W("Apinan Chatchai Ekkachai Jakkrit Kanchit Manit Nattapong Pichit Rungroj Surachai Tawan Weerachai Yodsak"),
                W("Siripong Wongsakul Jaidee Suksawat Charoen Prakob Tanakorn Intarat Maneewong Pongsiri Saetang Thammasat Udomsuk Vichai Yongyuth")),
            ["turkic"] = (
                W("Berk Tolga Alp Baris Eren Furkan Hakan Mert Orhan Samet Ismail"),
                W("Arslan Polat Korkmaz Ozkan Tekin Unal Yildiz Cetin Kurt Aksoy Gunes Bulut")),
            ["westafrican"] = (
                W("Kelechi Emeka Ikenna Babatunde Segun Kayode Yinka Seun Tobi Dayo Ibrahima Ousmane Cheikh Moussa Abdoulaye Kojo Kwesi Nana Ebo Kwabena Chukwudi Nnamdi Uche Ayodele Gbenga"),
                W("Eze Nnadi Fashola Adeleke Boateng Appiah Danso Traore Keita Camara Sow Cisse Coulibaly Sylla Fofana Bangura Kamara Ogunleye Olawale Abiodun Nwachukwu Ezeh Obasi Quansah Agyeman Yeboah Gyasi")),
        };
}
