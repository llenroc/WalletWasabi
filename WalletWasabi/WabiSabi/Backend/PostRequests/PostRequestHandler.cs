using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Helpers;
using WalletWasabi.Nito.AsyncEx;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Models;

namespace WalletWasabi.WabiSabi.Backend.PostRequests
{
	public class PostRequestHandler : IAsyncDisposable
	{
		public PostRequestHandler(WabiSabiConfig config, Prison prison, IArena arena, IRPCClient rpc)
		{
			Config = config;
			Prison = prison;
			Arena = arena;
			Rpc = rpc;
			Network = rpc.Network;
		}

		private bool DisposeStarted { get; set; } = false;
		private object DisposeStartedLock { get; } = new();
		private AbandonedTasks RunningRequests { get; } = new();
		public WabiSabiConfig Config { get; }
		public Prison Prison { get; }
		public IArena Arena { get; }
		public IRPCClient Rpc { get; }
		public Network Network { get; }

		public async Task<InputsRegistrationResponse> RegisterInputAsync(InputsRegistrationRequest request)
		{
			DisposeGuard();
			using (RunningTasks.RememberWith(RunningRequests))
			{
				if (!Arena.TryGetRound(request.RoundId, out var round))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound);
				}
				if (round.Phase != Phase.InputRegistration)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase);
				}
				var inputRoundSignaturePairs = request.InputRoundSignaturePairs;
				var inputs = inputRoundSignaturePairs.Select(x => x.Input);

				int inputCount = inputs.Count();
				if (inputCount != inputs.Distinct().Count())
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.NonUniqueInputs);
				}
				if (round.MaxInputCountByAlice < inputCount)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.TooManyInputs);
				}
				if (inputs.Any(x => Prison.TryGet(x, out var inmate) && (!Config.AllowNotedInputRegistration || inmate.Punishment != Punishment.Noted)))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputBanned);
				}
				if (round.IsBlameRound && inputs.Any(x => !round.BlameWhitelist.Contains(x)))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputNotWhitelisted);
				}

				var inputValueSum = Money.Zero;
				var inputWeightSum = 0;
				foreach (var inputRoundSignaturePair in inputRoundSignaturePairs)
				{
					OutPoint input = inputRoundSignaturePair.Input;
					var txOutResponse = await Rpc.GetTxOutAsync(input.Hash, (int)input.N, includeMempool: true).ConfigureAwait(false);
					if (txOutResponse is null)
					{
						throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputSpent);
					}
					if (txOutResponse.Confirmations == 0)
					{
						throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputUnconfirmed);
					}
					if (txOutResponse.IsCoinBase && txOutResponse.Confirmations <= 100)
					{
						throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputImmature);
					}
					if (!txOutResponse.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2WPKH))
					{
						throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.ScriptNotAllowed);
					}
					var address = (BitcoinWitPubKeyAddress)txOutResponse.TxOut.ScriptPubKey.GetDestinationAddress(Network);
					if (!address.VerifyMessage(round.Hash, inputRoundSignaturePair.RoundSignature))
					{
						throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongRoundSignature);
					}
					inputValueSum += txOutResponse.TxOut.Value;

					if (txOutResponse.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2WPKH))
					{
						// Convert conservative P2WPKH size in virtual bytes to weight units.
						inputWeightSum += Constants.P2wpkhInputVirtualSize * 4;
					}
					else
					{
						throw new NotImplementedException($"{txOutResponse.ScriptPubKeyType} weight estimation isn't implemented.");
					}
				}

				if (inputValueSum < round.MinRegistrableAmount)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.NotEnoughFunds);
				}
				if (inputValueSum > round.MaxRegistrableAmount)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.TooMuchFunds);
				}

				if (inputWeightSum < round.MinRegistrableWeight)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.NotEnoughWeight);
				}
				if (inputWeightSum > round.MaxRegistrableWeight)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.TooMuchWeight);
				}

				throw new NotImplementedException();
			}
		}

		public void RemoveInput(InputsRemovalRequest request)
		{
			DisposeGuard();
			using (RunningTasks.RememberWith(RunningRequests))
			{
				if (!Arena.TryGetRound(request.RoundId, out var round))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound);
				}
				if (round.Phase != Phase.InputRegistration)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase);
				}

				throw new NotImplementedException();
			}
		}

		public ConnectionConfirmationResponse ConfirmConnection(ConnectionConfirmationRequest request)
		{
			DisposeGuard();
			using (RunningTasks.RememberWith(RunningRequests))
			{
				if (!Arena.TryGetRound(request.RoundId, out var round))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound);
				}
				if (round.Phase != Phase.InputRegistration && round.Phase != Phase.ConnectionConfirmation)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase);
				}
				if (!round.TryGetAlice(request.AliceId, out var alice))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound);
				}

				throw new NotImplementedException();
			}
		}

		public OutputRegistrationResponse RegisterOutput(OutputRegistrationRequest request)
		{
			DisposeGuard();
			using (RunningTasks.RememberWith(RunningRequests))
			{
				if (!Arena.TryGetRound(request.RoundId, out var round))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound);
				}
				if (round.Phase != Phase.OutputRegistration)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase);
				}
				if (!request.Output.ScriptPubKey.IsScriptType(ScriptType.P2WPKH))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.ScriptNotAllowed);
				}

				throw new NotImplementedException();
			}
		}

		public void SignTransaction(TransactionSignaturesRequest request)
		{
			DisposeGuard();
			using (RunningTasks.RememberWith(RunningRequests))
			{
				if (!Arena.TryGetRound(request.RoundId, out var round))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound);
				}
				if (round.Phase != Phase.TransactionSigning)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase);
				}
				if (!round.TryGetAlice(request.AliceId, out var alice))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound);
				}

				throw new NotImplementedException();
			}
		}

		private void DisposeGuard()
		{
			lock (DisposeStartedLock)
			{
				if (DisposeStarted)
				{
					throw new ObjectDisposedException(nameof(PostRequestHandler));
				}
			}
		}

		public async ValueTask DisposeAsync()
		{
			lock (DisposeStartedLock)
			{
				DisposeStarted = true;
			}
			await RunningRequests.WhenAllAsync().ConfigureAwait(false);
		}
	}
}
