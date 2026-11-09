# Avaliacao 3 - Replicacao de dados

## Etapa 2: cliente e servidor gRPC com persistencia JSON

Aplicacao .NET 10 com dois processos: servidor gRPC e cliente de console.
O armazenamento JSON da etapa 1 foi preservado, inclusive o formato dos dados.
Ainda nao ha cluster, log de replicacao, quorum ou eleicao: cada gravacao e
efetivada imediatamente no unico servidor.

### Executar

Requer SDK .NET 10. Na raiz do repositorio, inicie o servidor:

```powershell
dotnet run --project avaliacao-3\src\Replication.Node -- --data avaliacao-3\data\node-1\state.json --port 7001
```

Em outro terminal, inicie o cliente:

```powershell
dotnet run --project avaliacao-3\src\Replication.Client -- --server http://127.0.0.1:7001
```

O endereco padrao do cliente e `http://127.0.0.1:7001`, e a porta padrao do
servidor e `7001`. O servidor aceita apenas conexoes locais IPv4, via HTTP/2
sem TLS (h2c), para desenvolvimento. Nao exponha essa configuracao na Internet.
O terminal do servidor exibe logs; o menu agora fica somente no cliente:

- `1`: informar a chave e depois o valor; uma chave existente e atualizada.
- `2`: consultar uma chave.
- `3`: listar os dados em ordem de chave.
- `0`: sair.

As chaves diferenciam maiusculas de minusculas e nao podem ser vazias ou conter
somente espacos. Os valores podem ser vazios. As entradas sao lidas como texto,
uma linha por campo.

O caminho informado ao servidor e relativo ao diretorio de execucao. Sem `--data`, o arquivo
fica em `data\state.json` dentro do diretorio do executavel. O caminho absoluto
e exibido na inicializacao. Use o mesmo caminho para recuperar os dados.

### Verificar a recuperacao apos reinicio

1. Inicie o servidor e o cliente nos terminais separados.
2. No cliente, escolha `1`, informe `produto:1` e depois `Teclado`.
3. Escolha `2` e consulte `produto:1`; escolha `3` para listar.
4. Saia do cliente com `0` e encerre o servidor com `Ctrl+C`.
5. Inicie novamente os dois processos com os mesmos comandos.
6. Consulte `produto:1` para verificar que o dado foi recuperado do JSON.

### Contrato e tratamento de erros

O [storage.proto](src/Replication.Contracts/Protos/storage.proto) define tres RPCs:

| RPC | Entrada | Resultado |
| --- | --- | --- |
| `Write` | Chave e valor | Resposta vazia de sucesso, somente apos persistir |
| `Read` | Chave | Valor, ou status `NotFound` |
| `List` | Requisicao vazia | Entradas ordenadas por chave |

O projeto `Replication.Contracts` gera as mensagens, a base do servidor e o
cliente gRPC a partir do contrato. O servidor usa ASP.NET Core gRPC; o cliente
usa `Grpc.Net.Client`. Nao ha REST ou acesso do cliente ao arquivo JSON.

Chaves invalidas sao rejeitadas tambem no servidor com `InvalidArgument`.
Valor vazio e permitido e nao se confunde com chave inexistente.
Falhas de persistencia retornam `Internal`, sem confirmar sucesso ou alterar
os dados em memoria; os detalhes ficam nos logs do servidor.

Cada chamada do cliente tem deadline de cinco segundos, configuravel:

```powershell
dotnet run --project avaliacao-3\src\Replication.Client -- --server http://127.0.0.1:7001 --timeout-seconds 10
```

O cliente informa chave inexistente, servidor indisponivel e timeout, mantendo
o menu disponivel. `Ctrl+C` cancela uma chamada em andamento. Nao ha repeticao
automatica: se a comunicacao falhar durante uma escrita, o servidor pode ja
ter persistido o dado. Consulte a chave antes de repetir.

### Formato persistido

```json
{
  "data": {
    "produto:1": "Teclado"
  }
}
```

O armazenamento usa `System.Text.Json`, sem banco de dados ou dependencias
externas de persistencia. A logica fica em
[JsonKeyValueStore](src/Replication.Node/Storage/JsonKeyValueStore.cs),
separada do [servico gRPC](src/Replication.Node/Services/StorageService.cs) e do
[menu do cliente](src/Replication.Client/Program.cs).

Cada atualizacao grava uma nova versao completa em um arquivo `.tmp`, faz
flush para disco e substitui o JSON oficial no mesmo diretorio. A memoria so e
atualizada depois da substituicao. Isso evita sobrescrever parcialmente o
arquivo oficial; nao constitui garantia absoluta contra perda de energia ou
falhas de hardware. O custo de reescrever todos os dados e adequado a esta etapa.

Um arquivo `.lock`, mantido aberto com acesso exclusivo, impede duas instancias
de usarem simultaneamente o mesmo estado. Ele pode permanecer no disco apos a
saida: o bloqueio e liberado ao fechar o processo, nao ao apagar o arquivo.
Cada futuro no deve usar seu proprio caminho.

JSON invalido interrompe a inicializacao sem apagar os dados. Falhas de escrita
sao registradas e retornadas ao cliente sem confirmar sucesso. Um `.tmp` abandonado
nao e promovido automaticamente; a proxima escrita o substitui.
Os dados da pasta local `data` nao devem ser versionados.

### Compilar e testar

```powershell
dotnet build avaliacao-3\Replication.slnx
dotnet test avaliacao-3\tests\Replication.Tests
```

Os testes locais da etapa 1 cobrem gravacao, atualizacao, recuperacao, validacao,
exclusividade, falha de escrita, arquivo temporario abandonado e concorrencia.
Os testes gRPC iniciam um servidor real em porta dinamica e verificam os RPCs,
reinicio com recuperacao, validacao no servidor, falha de persistencia,
escritas concorrentes e indisponibilidade. Um transporte atrasado de teste
verifica o deadline sem introduzir atrasos artificiais no servidor de producao.
O [NuGet.Config](NuGet.Config) limita a restauracao desta avaliacao ao nuget.org,
incluindo os pacotes de teste, sem alterar a configuracao das outras avaliacoes.
A regra existente em [.gitignore](../.gitignore) ignora `avaliacao-3/tests/**`;
os testes permanecem locais e essa escolha de versionamento foi preservada.

### Proxima etapa

Separar o log de operacoes dos dados comitados, antes de introduzir replicacao.
Termo, voto, quorum e eleicao serao implementados gradualmente nas etapas seguintes.
