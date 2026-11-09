namespace Reparo.Core;

/// <summary>Códigos de saída do app. 0 = reparo concluído; qualquer outro = falhou ou foi interrompido.</summary>
public enum CodigoSaida
{
    Sucesso = 0,
    FalhaNoReparo = 1,
    CanceladoPeloOperador = 2,
    ErroInesperado = 3,
    ConfiguracaoInvalida = 4,

    /// <summary>O mais grave: falhou e não foi possível devolver o banco original ao lugar.</summary>
    FalhaAoDesfazer = 5,

    /// <summary>Usado só durante o desenvolvimento (tela ainda não pronta).</summary>
    NaoImplementado = 9,
}
