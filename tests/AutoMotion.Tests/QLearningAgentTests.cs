using AutoMotion.Models;

namespace AutoMotion.Tests;

public class QLearningAgentTests
{
    [Fact]
    public void NewAgent_HasNoLearnedStateActions()
    {
        var agent = new QLearningAgent(actionCount: 4);
        Assert.Equal(0, agent.KnownStateActionCount);
    }

    [Fact]
    public void Learn_UpdatesQValue_TowardsReward()
    {
        var agent = new QLearningAgent(actionCount: 4) { LearningRate = 0.5, DiscountFactor = 0.9 };

        agent.Learn(state: 0, action: 1, reward: 10, nextState: 1);

        // Q(0,1) = 0 + 0.5 * (10 + 0.9*0 - 0) = 5
        Assert.Equal(5.0, agent.GetQ(0, 1), precision: 3);
    }

    [Fact]
    public void Learn_RepeatedPositiveReward_ConvergesTowardRewardValue()
    {
        var agent = new QLearningAgent(actionCount: 2) { LearningRate = 0.5, DiscountFactor = 0.0 };

        for (int i = 0; i < 50; i++)
            agent.Learn(state: 0, action: 0, reward: 10, nextState: 0);

        Assert.True(agent.GetQ(0, 0) > 9.9);
    }

    [Fact]
    public void ChooseAction_WithZeroExploration_PicksHighestKnownQValue()
    {
        var agent = new QLearningAgent(actionCount: 3, random: new Random(42)) { ExplorationRate = 0.0 };
        agent.Learn(state: 5, action: 2, reward: 100, nextState: 0);

        int chosen = agent.ChooseAction(5);

        Assert.Equal(2, chosen);
    }

    [Fact]
    public void ChooseAction_WithFullExploration_StaysWithinActionRange()
    {
        var agent = new QLearningAgent(actionCount: 8, random: new Random(1)) { ExplorationRate = 1.0 };

        for (int i = 0; i < 50; i++)
        {
            int action = agent.ChooseAction(0);
            Assert.InRange(action, 0, 7);
        }
    }

    [Fact]
    public void Learn_UnknownStateAction_DefaultsToZeroBeforeUpdate()
    {
        var agent = new QLearningAgent(actionCount: 4);

        Assert.Equal(0.0, agent.GetQ(99, 1));
    }
}
