FROM scratch
COPY --chown=1000:1000 files/home/ /home/agent/
# The install lifecycle renders these with the tenant host into the agents' configs.
COPY --chown=0:0 files/templates/ /opt/github-enterprise-auth/templates/
# Login shells and ~/.bashrc source it: the Copilot CLI reads the token placeholder from it.
COPY --chown=0:0 files/etc/profile.d/ /etc/profile.d/
