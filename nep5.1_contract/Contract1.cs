﻿using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.ComponentModel;
using System.Numerics;

namespace Nep5_Contract
{
    public class ContractNep5_1 : SmartContract
    {
        //一个完整的块天应该 4块每分钟*60*24=5769，但15秒出块只是个理论值，肯定会慢很多
        public const ulong blockday = 4096;
        //取个整数吧，4096，
        public const ulong bonusInterval = blockday * 1;//发奖间隔
        public const int bonusCount = 7;
        //首先在nep5基础上将账户分为两个部分
        //一个部分是saving
        //saving 有一个产生块的标记
        //一个部分是cash
        //当你收到一笔转账，他算在cash里面
        //当你花费一笔钱，先扣cash，扣完再扣saving

        //余额是saving 和 cash 的总和

        //cash 如何转换为为saving
        //通过claim   指令
        //claim指令 将领奖并且把所有的余额转换为saving，产生块标记为当前块

        //*claim指令
        //claim指令取得已经公布的奖励，仅有自己的saving的block <奖励的startblock 才可以领奖
        //领奖后全部资产+领到的资产变成saving状态

        //消耗池
        //所有花费掉的资产进入消耗池，等候公布奖励
        //公布奖励
        //将现有奖励从消耗池中取出，变成一个奖励，奖励规定只有savingblock <startblock 才可以领取。
        //根据 消耗池数量/总发行数量 确定一个领奖比例。
        //将同时保持五个公布奖励。
        //当公布第六个奖励时，第一个奖励被删除，并将他的余额丢入消耗池

        //*检查奖励指令
        //程序约定好公布奖励的block间隔，当有人检查奖励并且奖励的block间隔已经大于等于程序设置，则公布奖励。
        //用户可以用检查奖励指令获知最近的五个奖励，以此可以推算，如果领奖自己可以获取多少收益。

        //增加两条指令
        //checkpool*检查奖励
        //claim*领取奖励

        //可循环分配资产
        //最终确定加四个接口（暂定名Nep5.1）
        //检查奖励，只读（everyone）
        public static object[] CheckBonus()
        {
            byte[] data = Storage.Get(Storage.CurrentContext, "!bonus:L");
            if (data.Length == 0)
                return null;




            BigInteger lastBounsBlock = Helper.AsBigInteger(data);
            object[] retarray = new object[5];

            for (var i = 0; i < bonusCount; i++)
            {
                byte[] bIndex = Helper.AsByteArray("bonus").Concat(Helper.AsByteArray(lastBounsBlock));
                byte[] bStartBlock = bIndex.Concat(Helper.AsByteArray(":S"));
                byte[] bBonusValue = bIndex.Concat(Helper.AsByteArray(":V"));
                byte[] bBonusCount = bIndex.Concat(Helper.AsByteArray(":C"));
                byte[] bLastIndex = bIndex.Concat(Helper.AsByteArray(":L"));
                byte[] StartBlock = Storage.Get(Storage.CurrentContext, bStartBlock);
                byte[] BonusValue = Storage.Get(Storage.CurrentContext, bBonusValue);
                byte[] BonusCount = Storage.Get(Storage.CurrentContext, bBonusCount);
                byte[] LastIndex = Storage.Get(Storage.CurrentContext, bLastIndex);
                object[] bonusItem = new object[4];
                bonusItem[0] = StartBlock;
                bonusItem[1] = BonusCount;
                bonusItem[2] = BonusValue;
                bonusItem[3] = LastIndex;
                retarray[i] = bonusItem;
                if (LastIndex.Length == 0)
                    break;
                lastBounsBlock = Helper.AsBigInteger(LastIndex);
                if (lastBounsBlock == 0)
                    break;
            }
            return retarray;
        }
        //消耗资产（个人）
        public static bool Use(byte[] from, BigInteger value)
        {
            if (value <= 0) return false;
            if (!Runtime.CheckWitness(from)) return false;
            var indexcash = from.Concat(new byte[] { 0 });
            var indexsaving = from.Concat(new byte[] { 1 });

            BigInteger from_value_cash = Storage.Get(Storage.CurrentContext, indexcash).AsBigInteger();
            BigInteger from_value_saving = Storage.Get(Storage.CurrentContext, indexsaving).AsBigInteger();
            var balance = from_value_cash + from_value_saving * factor;
            if (balance < value) return false;

            if (from_value_cash >= value)//零钱就够扣了
            {
                Storage.Put(Storage.CurrentContext, indexcash, from_value_cash - value);
            }
            else//零钱不够扣
            {
                var lastv = balance - value;
                var bigN = lastv / (factor);
                var smallN = lastv % (factor);
                Storage.Put(Storage.CurrentContext, indexcash, smallN);
                Storage.Put(Storage.CurrentContext, indexsaving, bigN);
            }

            byte[] data = Storage.Get(Storage.CurrentContext, "!pool");
            BigInteger v = Helper.AsBigInteger(data);
            v += value;
            Storage.Put(Storage.CurrentContext, "!pool", v);

            return true;
        }
        //新奖励，（everyone）随便调用，不符合规则就不会创建奖励，谁都可以调用这个，催促发奖励。
        public static BigInteger NewBonus()
        {
            byte[] data = Storage.Get(Storage.CurrentContext, "!bonus:L");
            //if (data.Length == 0)
            //    ;

            BigInteger index = Blockchain.GetHeight() / bonusInterval;
            if (index < 1)
                return 0;

            BigInteger bounsheight = (index - 1) * bonusInterval;

            BigInteger lastBounsBlock = Helper.AsBigInteger(data);
            if (bounsheight == lastBounsBlock)
                return 0;

            //清掉奖池
            var poolv = Storage.Get(Storage.CurrentContext, "!pool").AsBigInteger();
            Storage.Delete(Storage.CurrentContext, "!pool");
            byte[] bIndex = Helper.AsByteArray("bonus").Concat(Helper.AsByteArray(bounsheight));
            byte[] bStartBlock = bIndex.Concat(Helper.AsByteArray(":S"));
            byte[] bBonusValue = bIndex.Concat(Helper.AsByteArray(":V"));
            byte[] bBonusCount = bIndex.Concat(Helper.AsByteArray(":C"));
            byte[] bLastIndex = bIndex.Concat(Helper.AsByteArray(":L"));
            Storage.Put(Storage.CurrentContext, bStartBlock, bounsheight.AsByteArray());
            Storage.Put(Storage.CurrentContext, bBonusCount, poolv.AsByteArray());
            BigInteger bonusV = poolv / (TotalSupply() / factor);//整数部分一发几
            Storage.Put(Storage.CurrentContext, bBonusValue, bonusV.AsByteArray());
            Storage.Put(Storage.CurrentContext, bLastIndex, lastBounsBlock.AsByteArray());

            //写入lastblock
            Storage.Put(Storage.CurrentContext, "!bonus:L", bounsheight.AsByteArray());
            return bounsheight;
        }
        //领取奖励（个人）
        public static BigInteger GetBonus(byte[] to)
        {
            byte[] data = Storage.Get(Storage.CurrentContext, "!bonus:L");
            if (data.Length == 0)
                return 0;

            var indexcashto = to.Concat(new byte[] { 0 });
            var indexsavingto = to.Concat(new byte[] { 1 });
            var indexsavingblockto = to.Concat(new byte[] { 2 });
            BigInteger to_value_cash = Storage.Get(Storage.CurrentContext, indexcashto).AsBigInteger();
            BigInteger to_value_saving = Storage.Get(Storage.CurrentContext, indexsavingto).AsBigInteger();
            BigInteger to_value_savingblock = Storage.Get(Storage.CurrentContext, indexsavingblockto).AsBigInteger();

            BigInteger lastBonusBlock = Helper.AsBigInteger(data);
            BigInteger addValue = 0;
            for (var i = 0; i < bonusCount; i++)
            {
                byte[] bIndex = Helper.AsByteArray("bonus").Concat(Helper.AsByteArray(lastBonusBlock));
                byte[] bStartBlock = bIndex.Concat(Helper.AsByteArray(":S"));
                byte[] bBonusValue = bIndex.Concat(Helper.AsByteArray(":V"));
                byte[] bBonusCount = bIndex.Concat(Helper.AsByteArray(":C"));
                byte[] bLastIndex = bIndex.Concat(Helper.AsByteArray(":L"));
                var StartBlock = Storage.Get(Storage.CurrentContext, bStartBlock).AsBigInteger();
                var BonusValue = Storage.Get(Storage.CurrentContext, bBonusValue).AsBigInteger();
                var BonusCount = Storage.Get(Storage.CurrentContext, bBonusCount).AsBigInteger();
                if (to_value_savingblock < StartBlock)//有领奖资格
                {
                    var cangot = to_value_saving * BonusValue;//要领走多少
                    addValue += cangot;
                    Storage.Put(Storage.CurrentContext, bBonusCount, bonusCount - cangot);
                }
                byte[] LastIndex = Storage.Get(Storage.CurrentContext, bLastIndex);
                if (LastIndex.Length == 0)
                    break;
                lastBonusBlock = Helper.AsBigInteger(LastIndex);
                if (lastBonusBlock == 0)
                    break;
            }
            //领奖写入
            BigInteger balanceto = to_value_saving * factor + to_value_cash;
            {
                var lastv = balanceto + addValue;
                var bigN = lastv / (factor);
                var smallN = lastv % (factor);
                Storage.Put(Storage.CurrentContext, indexcashto, smallN);
                Storage.Put(Storage.CurrentContext, indexsavingto, bigN);
                BigInteger block = (Blockchain.GetHeight());
                Storage.Put(Storage.CurrentContext, indexsavingblockto, block);
            }
            return 0;
        }

        //nep5 notify
        public delegate void deleTransfer(byte[] from, byte[] to, BigInteger value);
        [DisplayName("transfer")]
        public static event deleTransfer Transferred;

        public static readonly byte[] SuperAdmin = Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");

        //nep5 func
        public static BigInteger TotalSupply()
        {
            return Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        }
        public static string Name()
        {
            return "NNS Coin";
        }
        public static string Symbol()
        {
            return "NNS";
        }
        private const ulong factor = 100000000;
        private const ulong totalCoin = 100000000 * factor;
        public static byte Decimals()
        {
            return 8;
        }
        public static BigInteger BalanceOf(byte[] address)
        {
            var indexcash = address.Concat(new byte[] { 0 });
            var indexsaving = address.Concat(new byte[] { 1 });
            BigInteger from_value_cash = Storage.Get(Storage.CurrentContext, indexcash).AsBigInteger();
            BigInteger from_value_saving = Storage.Get(Storage.CurrentContext, indexsaving).AsBigInteger();
            var balance = from_value_cash + from_value_saving * factor;
            return balance;
        }
        public static BigInteger[] BalanceOfDetail(byte[] address)
        {
            var indexcash = address.Concat(new byte[] { 0 });
            var indexsaving = address.Concat(new byte[] { 1 });
            var indexsavingblock = address.Concat(new byte[] { 2 });
            BigInteger from_value_cash = Storage.Get(Storage.CurrentContext, indexcash).AsBigInteger();
            BigInteger from_value_saving = Storage.Get(Storage.CurrentContext, indexsaving).AsBigInteger();
            BigInteger from_value_savingblock = Storage.Get(Storage.CurrentContext, indexsavingblock).AsBigInteger();
            var balance = from_value_cash + from_value_saving * factor;
            BigInteger[] ret = new BigInteger[4];
            ret[0] = from_value_cash;
            ret[1] = from_value_saving;
            ret[2] = from_value_savingblock;
            ret[3] = balance;
            return ret;
        }
        public static bool Transfer(byte[] from, byte[] to, BigInteger value)
        {
            if (value <= 0) return false;
            if (!Runtime.CheckWitness(from)) return false;
            if (from == to) return true;

            var indexcash = from.Concat(new byte[] { 0 });
            var indexsaving = from.Concat(new byte[] { 1 });
            BigInteger from_value_cash = Storage.Get(Storage.CurrentContext, indexcash).AsBigInteger();
            BigInteger from_value_saving = Storage.Get(Storage.CurrentContext, indexsaving).AsBigInteger();
            var balance = from_value_cash + from_value_saving * factor;

            if (balance < value) return false;
            if (from_value_cash >= value)//零钱就够扣了
            {
                Storage.Put(Storage.CurrentContext, indexcash, from_value_cash - value);
            }
            else//零钱不够扣
            {
                var lastv = balance - value;
                var bigN = lastv / (factor);
                var smallN = lastv % (factor);
                Storage.Put(Storage.CurrentContext, indexcash, smallN);
                Storage.Put(Storage.CurrentContext, indexsaving, bigN);
            }

            var indexcashto = to.Concat(new byte[] { 0 });
            var indexsavingto = to.Concat(new byte[] { 1 });
            var indexsavingblockto = to.Concat(new byte[] { 2 });
            BigInteger to_value_cash = Storage.Get(Storage.CurrentContext, indexcashto).AsBigInteger();
            BigInteger to_value_saving = Storage.Get(Storage.CurrentContext, indexsavingto).AsBigInteger();
            var balanceto = to_value_cash + to_value_saving * factor;
            if (to_value_saving == 0)//无存款账户，帮他存了
            {
                var lastv = balanceto + value;
                var bigN = lastv / (factor);
                var smallN = lastv % (factor);
                Storage.Put(Storage.CurrentContext, indexcashto, smallN);
                Storage.Put(Storage.CurrentContext, indexsavingto, bigN);
                BigInteger block = (Blockchain.GetHeight());
                Storage.Put(Storage.CurrentContext, indexsavingblockto, block);
            }
            else
            {
                Storage.Put(Storage.CurrentContext, indexcashto, to_value_cash + value);
            }
            Transferred(from, to, value);
            return true;
        }

        public static object Main(string method, object[] args)
        {
            var magicstr = "2018-01-03";

            if (Runtime.Trigger == TriggerType.Verification)//取钱才会涉及这里
            {
                return Runtime.CheckWitness(SuperAdmin);
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                //this is in nep5
                if (method == "totalSupply") return TotalSupply();
                if (method == "name") return Name();
                if (method == "symbol") return Symbol();
                if (method == "decimals") return Decimals();
                if (method == "balanceOf")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return BalanceOf(account);
                }
                if (method == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];
                    return Transfer(from, to, value);
                }
                //this is add

                if (method == "deploy")//fix count
                {
                    if (args.Length != 1) return false;
                    if (!Runtime.CheckWitness(SuperAdmin)) return false;
                    byte[] total_supply = Storage.Get(Storage.CurrentContext, "totalSupply");
                    if (total_supply.Length != 0) return false;

                    var indexcashto = SuperAdmin.Concat(new byte[] { 0 });

                    Storage.Put(Storage.CurrentContext, indexcashto, totalCoin);
                    Storage.Put(Storage.CurrentContext, "totalSupply", totalCoin);
                    Transferred(null, SuperAdmin, totalCoin);
                }
                if (method == "balanceOfDetail")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return BalanceOfDetail(account);
                }
                if (method == "use")
                {
                    if (args.Length != 2) return false;
                    byte[] from = (byte[])args[0];
                    BigInteger value = (BigInteger)args[1];
                    return Use(from, value);
                }
                if (method == "getBonus")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return GetBonus(account);
                }
                if (method == "checkBonus")
                {
                    return CheckBonus();
                }
                if (method == "checkBonusAndNew")
                {
                    return NewBonus();
                }
            }
            return false;
        }

    }
}
