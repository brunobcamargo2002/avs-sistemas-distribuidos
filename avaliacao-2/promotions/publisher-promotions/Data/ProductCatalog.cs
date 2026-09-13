namespace publisher_promotions.Data;

/// <summary>
/// Catálogo de categorias e produtos de uma loja de informática.
/// As categorias usam notação hierárquica (ex.: hardware.placa_de_video)
/// para compor routing keys como promocao.categoria.hardware.placa_de_video.
/// </summary>
public static class ProductCatalog
{
    public static readonly (string Category, string Name)[] Products =
    [
        ("hardware.placa_de_video", "Placa de Vídeo RTX 4060"),
        ("hardware.placa_de_video", "Placa de Vídeo RX 7600"),
        ("hardware.processador", "Processador Intel Core i5-13400"),
        ("hardware.processador", "Processador AMD Ryzen 5 7600"),
        ("hardware.placa_mae", "Placa Mãe B450M Gaming"),
        ("hardware.placa_mae", "Placa Mãe Z790 AORUS"),
        ("hardware.memoria_ram", "Memória RAM 16GB DDR4 3200MHz"),
        ("hardware.memoria_ram", "Memória RAM 32GB DDR5 6000MHz"),
        ("hardware.armazenamento", "SSD NVMe 1TB"),
        ("hardware.armazenamento", "HD 2TB SATA"),
        ("hardware.fonte", "Fonte 650W 80 Plus Bronze"),
        ("hardware.gabinete", "Gabinete Gamer ATX RGB"),
        ("hardware.cooler", "Water Cooler 240mm RGB"),
        ("perifericos.teclado", "Teclado Mecânico RGB"),
        ("perifericos.mouse", "Mouse Gamer 16000 DPI"),
        ("perifericos.monitor", "Monitor 27\" 144Hz IPS"),
        ("perifericos.headset", "Headset Gamer 7.1"),
        ("perifericos.webcam", "Webcam Full HD 1080p"),
        ("notebooks", "Notebook Gamer i7 RTX 4050"),
        ("notebooks", "Notebook Ultrafino i5"),
        ("redes.roteador", "Roteador Wi-Fi 6 AX3000"),
        ("redes.switch", "Switch Gerenciável 8 Portas"),
        ("software.antivirus", "Licença Antivírus Anual"),
        ("software.sistema_operacional", "Licença Windows 11 Pro"),
        ("acessorios.cadeira_gamer", "Cadeira Gamer Ergonômica"),
        ("acessorios.mousepad", "Mousepad Gamer XL"),
    ];
}
