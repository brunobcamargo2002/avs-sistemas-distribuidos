# Avaliacao 3 - Replicacao de dados

## Etapa 1: armazenamento local com JSON

Aplicacao de console em .NET 10, ainda sem cliente separado, gRPC, cluster,
log de replicacao ou eleicao. Cada gravacao e efetivada imediatamente.

### Executar

Requer SDK .NET 10. Na raiz do repositorio:

```powershell
dotnet run --project avaliacao-3\src\Replication.Node -- --data avaliacao-3\data\node-1\state.json
```

O menu permite gravar, consultar e listar pares chave-valor:

- `1`: informar a chave e depois o valor; uma chave existente e atualizada.
- `2`: consultar uma chave.
- `3`: listar os dados em ordem de chave.
- `0`: sair.

As chaves diferenciam maiusculas de minusculas e nao podem ser vazias ou conter
somente espacos. Os valores podem ser vazios. As entradas sao lidas como texto,
uma linha por campo.

O caminho informado e relativo ao diretorio de execucao. Sem `--data`, o arquivo
fica em `data\state.json` dentro do diretorio do executavel. O caminho absoluto
e exibido na inicializacao. Use o mesmo caminho para recuperar os dados.

### Verificar a recuperacao apos reinicio

1. Execute o comando acima.
2. Escolha `1`, informe `produto:1` e depois `Teclado`.
3. Escolha `2` e consulte `produto:1`.
4. Saia com `0`.
5. Execute novamente o mesmo comando e consulte `produto:1`.
6. Escolha `3` para listar os dados recuperados.

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
separada do [menu de console](src/Replication.Node/Program.cs).

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
sao exibidas e encerram o programa sem confirmar sucesso. Um `.tmp` abandonado
nao e promovido automaticamente; a proxima escrita o substitui.
Os dados da pasta local `data` nao devem ser versionados.

### Compilar e testar

```powershell
dotnet build avaliacao-3\Replication.slnx
dotnet test avaliacao-3\tests\Replication.Tests
```

Os testes cobrem gravacao, atualizacao, recuperacao, validacao, exclusividade,
falha de escrita, arquivo temporario abandonado e concorrencia.
O [NuGet.Config](NuGet.Config) limita a restauracao desta avaliacao ao nuget.org,
incluindo os pacotes de teste, sem alterar a configuracao das outras avaliacoes.

### Proxima etapa

Adicionar um cliente separado e um servidor gRPC, reutilizando o armazenamento.
Termo, voto, log e commit serao introduzidos nas etapas de replicacao e eleicao.
